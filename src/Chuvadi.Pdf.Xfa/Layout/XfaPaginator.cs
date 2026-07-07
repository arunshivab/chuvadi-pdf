// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — pagination: contentArea sequencing, pageSet occurrence
//        (orderedOccurrence, duplexPaginated, simplexPaginated), forced breaks
//        (breakBefore / breakAfter), and keep constraints (keep-with-previous /
//        keep-with-next, honored via tentative placement with rollback).
// PHASE: LA-23b Phases C + C2 — pagination.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Xfa.Model;

namespace Chuvadi.Pdf.Xfa.Layout;

/// <summary>A single output page composed by the paginator.</summary>
internal sealed class XfaComposedPage
{
    internal XfaComposedPage(XfaPageArea area)
    {
        Area = area;
    }

    /// <summary>Gets the page area whose geometry this page uses.</summary>
    internal XfaPageArea Area { get; }

    /// <summary>Gets the boxes placed on this page, in device space.</summary>
    internal List<XfaBox> Boxes { get; } = new List<XfaBox>();
}

/// <summary>
/// Composes a parsed (and data-merged) template into a sequence of pages.
/// Flowed root content fills the content areas of the current page in document
/// order; when all content areas are full, a new page is instantiated per the
/// page set's relation: <see cref="XfaPageSetRelation.OrderedOccurrence"/>
/// walks the page areas honoring occurrence counts, while
/// <see cref="XfaPageSetRelation.DuplexPaginated"/> /
/// <see cref="XfaPageSetRelation.SimplexPaginated"/> select page areas by page
/// parity (<see cref="XfaPageArea.OddOrEven"/>). Forced breaks advance to the
/// next content area or page, and keep constraints
/// (<see cref="XfaNode.KeepNext"/> / <see cref="XfaNode.KeepPrevious"/>) bind
/// neighbouring blocks into groups that are re-placed together when a region
/// transition would split them.
/// </summary>
internal sealed class XfaPaginator
{
    private readonly List<XfaPageArea> _pageAreas;
    private readonly XfaPageSetRelation _relation;
    private readonly Dictionary<int, int> _instanceCounts = new Dictionary<int, int>();
    private readonly List<XfaComposedPage> _pages = new List<XfaComposedPage>();

    private int _areaCursor;
    private int _instancesOfCurrentArea;
    private int _contentAreaIndex;
    private double _penY;
    private bool _currentPageHasContent;

    private XfaPaginator(List<XfaPageArea> pageAreas, XfaPageSetRelation relation)
    {
        _pageAreas = pageAreas;
        _relation = relation;
    }

    /// <summary>
    /// Composes the pages for a template root. The root's page set supplies the
    /// page geometry; when the template has none, a default US-Letter page with
    /// a full-page content area is synthesized.
    /// </summary>
    /// <param name="root">The parsed template root subform.</param>
    /// <returns>The composed pages in order.</returns>
    internal static IReadOnlyList<XfaComposedPage> Compose(XfaSubform root)
    {
        ArgumentNullException.ThrowIfNull(root);

        XfaPageSet? pageSet = FindFirstPageSet(root);
        List<XfaPageArea> pageAreas = CollectPageAreas(pageSet);
        if (pageAreas.Count == 0)
        {
            pageAreas.Add(DefaultPageArea());
        }

        XfaPaginator paginator = new XfaPaginator(
            pageAreas, pageSet?.Relation ?? XfaPageSetRelation.OrderedOccurrence);
        paginator.NewPage();
        paginator.ComposeRoot(root);
        return paginator._pages;
    }

    private void ComposeRoot(XfaSubform root)
    {
        bool flowRoot = root.Layout is XfaLayout.TopToBottom or XfaLayout.Tb;

        List<XfaNode> blocks = new List<XfaNode>();
        foreach (XfaNode child in root.Children)
        {
            if (child is XfaPageSet or XfaPageArea or XfaContentArea
                || child.Presence is XfaPresence.Hidden or XfaPresence.Inactive)
            {
                continue;
            }

            if (flowRoot)
            {
                blocks.Add(child);
            }
            else
            {
                ComposePositionedBlock(child);
            }
        }

        if (!flowRoot)
        {
            return;
        }

        foreach ((List<XfaNode> group, XfaKeepScope scope) in GroupByKeep(blocks))
        {
            ComposeGroup(group, scope);
        }
    }

    // Binds consecutive blocks into keep-groups: a block with keep-next binds to
    // its successor; a block with keep-previous binds to its predecessor. The
    // group's scope is the widest scope among its binding constraints.
    private static List<(List<XfaNode> Group, XfaKeepScope Scope)> GroupByKeep(List<XfaNode> blocks)
    {
        List<(List<XfaNode> Group, XfaKeepScope Scope)> groups =
            new List<(List<XfaNode> Group, XfaKeepScope Scope)>();

        int i = 0;
        while (i < blocks.Count)
        {
            List<XfaNode> group = new List<XfaNode> { blocks[i] };
            XfaKeepScope scope = XfaKeepScope.None;

            while (i + 1 < blocks.Count)
            {
                XfaKeepScope bindForward = blocks[i].KeepNext;
                XfaKeepScope bindBackward = blocks[i + 1].KeepPrevious;
                XfaKeepScope bind = Widest(bindForward, bindBackward);
                if (bind == XfaKeepScope.None)
                {
                    break;
                }

                scope = Widest(scope, bind);
                i++;
                group.Add(blocks[i]);
            }

            groups.Add((group, scope));
            i++;
        }

        return groups;
    }

    private static XfaKeepScope Widest(XfaKeepScope a, XfaKeepScope b) =>
        (XfaKeepScope)Math.Max((int)a, (int)b);

    // Places a keep-group. Single unconstrained blocks place directly. Bound
    // groups place tentatively; if a region transition splits the group beyond
    // its scope, the tentative placement is rolled back, the pager advances to
    // a fresh region, and the group is re-placed (accepted on the second pass
    // to guarantee forward progress even for over-sized groups).
    private void ComposeGroup(List<XfaNode> group, XfaKeepScope scope)
    {
        if (group.Count == 1 && scope == XfaKeepScope.None)
        {
            ComposeFlowedBlock(group[0]);
            return;
        }

        PaginatorSnapshot snapshot = TakeSnapshot();
        List<(int Page, int ContentArea)> placements = PlaceGroup(group);

        if (!ViolatesScope(placements, scope))
        {
            return;
        }

        Rollback(snapshot);
        if (scope == XfaKeepScope.PageArea)
        {
            NewPage();
        }
        else
        {
            AdvanceContentArea();
        }

        PlaceGroup(group);
    }

    private List<(int Page, int ContentArea)> PlaceGroup(List<XfaNode> group)
    {
        List<(int Page, int ContentArea)> placements = new List<(int Page, int ContentArea)>();
        foreach (XfaNode member in group)
        {
            placements.Add(ComposeFlowedBlock(member));
        }

        return placements;
    }

    private static bool ViolatesScope(
        List<(int Page, int ContentArea)> placements, XfaKeepScope scope)
    {
        if (scope == XfaKeepScope.None || placements.Count < 2)
        {
            return false;
        }

        (int firstPage, int firstArea) = placements[0];
        foreach ((int page, int area) in placements)
        {
            if (scope == XfaKeepScope.PageArea && page != firstPage)
            {
                return true;
            }

            if (scope == XfaKeepScope.ContentArea
                && (page != firstPage || area != firstArea))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class PaginatorSnapshot
    {
        internal int PageCount;
        internal List<int> BoxCounts = new List<int>();
        internal int AreaCursor;
        internal int InstancesOfCurrentArea;
        internal int ContentAreaIndex;
        internal double PenY;
        internal bool CurrentPageHasContent;
        internal Dictionary<int, int> InstanceCounts = new Dictionary<int, int>();
    }

    private PaginatorSnapshot TakeSnapshot()
    {
        PaginatorSnapshot snapshot = new PaginatorSnapshot
        {
            PageCount = _pages.Count,
            AreaCursor = _areaCursor,
            InstancesOfCurrentArea = _instancesOfCurrentArea,
            ContentAreaIndex = _contentAreaIndex,
            PenY = _penY,
            CurrentPageHasContent = _currentPageHasContent,
            InstanceCounts = new Dictionary<int, int>(_instanceCounts),
        };

        foreach (XfaComposedPage page in _pages)
        {
            snapshot.BoxCounts.Add(page.Boxes.Count);
        }

        return snapshot;
    }

    private void Rollback(PaginatorSnapshot snapshot)
    {
        while (_pages.Count > snapshot.PageCount)
        {
            _pages.RemoveAt(_pages.Count - 1);
        }

        for (int i = 0; i < _pages.Count; i++)
        {
            List<XfaBox> boxes = _pages[i].Boxes;
            int keep = snapshot.BoxCounts[i];
            while (boxes.Count > keep)
            {
                boxes.RemoveAt(boxes.Count - 1);
            }
        }

        _areaCursor = snapshot.AreaCursor;
        _instancesOfCurrentArea = snapshot.InstancesOfCurrentArea;
        _contentAreaIndex = snapshot.ContentAreaIndex;
        _penY = snapshot.PenY;
        _currentPageHasContent = snapshot.CurrentPageHasContent;
        _instanceCounts.Clear();
        foreach (KeyValuePair<int, int> entry in snapshot.InstanceCounts)
        {
            _instanceCounts[entry.Key] = entry.Value;
        }
    }

    // A positioned (non-flowed) block is anchored to the current content area's
    // origin; it does not advance the flow pen.
    private void ComposePositionedBlock(XfaNode child)
    {
        XfaContentArea? contentArea = CurrentContentArea();
        double x = contentArea?.X.Points ?? 0.0;
        double y = contentArea?.Y.Points ?? 0.0;

        IReadOnlyList<XfaBox> boxes = XfaLayoutEngine.Layout(child, x, y);
        _pages[^1].Boxes.AddRange(boxes);
        _currentPageHasContent = _currentPageHasContent || boxes.Count > 0;
    }

    private (int Page, int ContentArea) ComposeFlowedBlock(XfaNode child)
    {
        if (child.BreakBefore is { } breakBefore)
        {
            Break(breakBefore);
        }

        (double marginTop, double marginBottom, _, _) = MarginsOf(child);
        double contentHeight = child.Height?.Points ?? MeasureHeight(child);
        double blockHeight = marginTop + contentHeight + marginBottom;

        (int pageIndex, int areaIndex, double x, double y) = Place(blockHeight);

        // The engine adds the child's own X (honored as a horizontal offset in
        // top-to-bottom flow) and its Y (cancelled: the flow pen governs y).
        IReadOnlyList<XfaBox> boxes = XfaLayoutEngine.Layout(
            child, x, y + marginTop - child.Y.Points);
        _pages[pageIndex].Boxes.AddRange(boxes);
        _currentPageHasContent = _currentPageHasContent || boxes.Count > 0;

        if (child.BreakAfter is { } breakAfter)
        {
            Break(breakAfter);
        }

        return (pageIndex, areaIndex);
    }

    // Places a block of the given height into the current content area,
    // advancing to the next content area / page when it does not fit.
    private (int PageIndex, int AreaIndex, double X, double Y) Place(double height)
    {
        while (true)
        {
            XfaContentArea? contentArea = CurrentContentArea();
            double areaHeight = contentArea?.Height?.Points ?? double.MaxValue;
            double remaining = areaHeight - _penY;

            // A block taller than an empty content area is placed anyway to
            // guarantee forward progress.
            if (height <= remaining || _penY <= 0.0)
            {
                double x = contentArea?.X.Points ?? 0.0;
                double y = (contentArea?.Y.Points ?? 0.0) + _penY;
                _penY += height;
                return (_pages.Count - 1, _contentAreaIndex, x, y);
            }

            AdvanceContentArea();
        }
    }

    private void Break(XfaBreakTarget target)
    {
        // A break before any content has been placed would create a blank
        // leading page / region; ignore it.
        if (!_currentPageHasContent && _contentAreaIndex == 0 && _penY <= 0.0)
        {
            return;
        }

        if (target == XfaBreakTarget.PageArea)
        {
            NewPage();
            return;
        }

        // ContentArea and Auto advance to the next available region.
        AdvanceContentArea();
    }

    private void AdvanceContentArea()
    {
        _contentAreaIndex++;
        _penY = 0.0;

        if (_contentAreaIndex >= ContentAreasOfCurrentPage().Count)
        {
            NewPage();
        }
    }

    private void NewPage()
    {
        XfaPageArea area = NextPageArea();
        _pages.Add(new XfaComposedPage(area));
        _contentAreaIndex = 0;
        _penY = 0.0;
        _currentPageHasContent = false;
    }

    private XfaPageArea NextPageArea()
    {
        return _relation switch
        {
            XfaPageSetRelation.DuplexPaginated => NextPageAreaByParity(duplex: true),
            XfaPageSetRelation.SimplexPaginated => NextPageAreaByParity(duplex: false),
            _ => NextPageAreaOrdered(),
        };
    }

    // Walks the page areas per orderedOccurrence: each area is used up to its
    // MaxOccur count (-1 = unbounded), then the cursor advances. An exhausted
    // sequence reuses the final area so overflow always has a target.
    private XfaPageArea NextPageAreaOrdered()
    {
        XfaPageArea current = _pageAreas[_areaCursor];

        if (_pages.Count == 0)
        {
            _instancesOfCurrentArea = 1;
            return current;
        }

        bool unbounded = current.MaxOccur < 0;
        if (unbounded || _instancesOfCurrentArea < current.MaxOccur)
        {
            _instancesOfCurrentArea++;
            return current;
        }

        if (_areaCursor < _pageAreas.Count - 1)
        {
            _areaCursor++;
            _instancesOfCurrentArea = 1;
            return _pageAreas[_areaCursor];
        }

        // Sequence exhausted: keep reusing the last area.
        _instancesOfCurrentArea++;
        return current;
    }

    // Duplex: the next page's parity (1-based) selects among page areas whose
    // oddOrEven matches (Any matches both). Simplex: every page is a front
    // (odd) page. Occurrence counts are honored per area; when every matching
    // area is exhausted, the last match is reused so overflow always lands.
    private XfaPageArea NextPageAreaByParity(bool duplex)
    {
        int nextNumber = _pages.Count + 1;
        bool odd = !duplex || (nextNumber % 2) == 1;

        XfaPageArea? lastMatch = null;
        for (int i = 0; i < _pageAreas.Count; i++)
        {
            XfaPageArea area = _pageAreas[i];
            if (!ParityMatches(area.OddOrEven, odd))
            {
                continue;
            }

            lastMatch = area;
            int used = _instanceCounts.TryGetValue(i, out int count) ? count : 0;
            if (area.MaxOccur < 0 || used < area.MaxOccur)
            {
                _instanceCounts[i] = used + 1;
                return area;
            }
        }

        return lastMatch ?? _pageAreas[^1];
    }

    private static bool ParityMatches(XfaOddOrEven oddOrEven, bool odd) => oddOrEven switch
    {
        XfaOddOrEven.Odd => odd,
        XfaOddOrEven.Even => !odd,
        _ => true,
    };

    private XfaContentArea? CurrentContentArea()
    {
        List<XfaContentArea> areas = ContentAreasOfCurrentPage();
        if (areas.Count == 0)
        {
            return null;
        }

        int index = Math.Min(_contentAreaIndex, areas.Count - 1);
        return areas[index];
    }

    private List<XfaContentArea> ContentAreasOfCurrentPage()
    {
        List<XfaContentArea> areas = new List<XfaContentArea>();
        foreach (XfaNode child in _pages[^1].Area.Children)
        {
            if (child is XfaContentArea contentArea)
            {
                areas.Add(contentArea);
            }
        }

        return areas;
    }

    private static double MeasureHeight(XfaNode child)
    {
        IReadOnlyList<XfaBox> probe = XfaLayoutEngine.Layout(child, 0.0, -child.Y.Points);
        double bottom = 0.0;
        foreach (XfaBox box in probe)
        {
            bottom = Math.Max(bottom, box.Bottom);
        }

        return bottom;
    }

    private static (double Top, double Bottom, double Left, double Right) MarginsOf(XfaNode node)
    {
        if (node.Margin is null)
        {
            return (0, 0, 0, 0);
        }

        return (node.Margin.Top.Points, node.Margin.Bottom.Points,
            node.Margin.Left.Points, node.Margin.Right.Points);
    }

    // Collects the page areas of the given pageSet, in document order.
    private static List<XfaPageArea> CollectPageAreas(XfaPageSet? pageSet)
    {
        List<XfaPageArea> areas = new List<XfaPageArea>();
        if (pageSet is null)
        {
            return areas;
        }

        foreach (XfaNode child in pageSet.Children)
        {
            if (child is XfaPageArea area)
            {
                areas.Add(area);
            }
        }

        return areas;
    }

    private static XfaPageSet? FindFirstPageSet(XfaNode node)
    {
        if (node is XfaPageSet pageSet)
        {
            return pageSet;
        }

        foreach (XfaNode child in node.Children)
        {
            XfaPageSet? found = FindFirstPageSet(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static XfaPageArea DefaultPageArea()
    {
        XfaPageArea area = new XfaPageArea
        {
            MediumShort = new XfaMeasurement(612.0),
            MediumLong = new XfaMeasurement(792.0),
            MaxOccur = -1,
        };
        XfaContentArea content = new XfaContentArea
        {
            Width = new XfaMeasurement(612.0),
            Height = new XfaMeasurement(792.0),
        };
        area.AddChild(content);
        return area;
    }
}
