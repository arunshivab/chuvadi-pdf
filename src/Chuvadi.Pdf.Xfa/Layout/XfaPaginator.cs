// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — pagination: contentArea sequencing, pageSet occurrence
//        (orderedOccurrence), and forced breaks (breakBefore / breakAfter).
// PHASE: LA-23b Phase C — pagination.

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
/// page set's occurrence rules (orderedOccurrence). Forced breaks
/// (<see cref="XfaNode.BreakBefore"/> / <see cref="XfaNode.BreakAfter"/>)
/// advance to the next content area or page.
/// </summary>
internal sealed class XfaPaginator
{
    private readonly List<XfaPageArea> _pageAreas;
    private readonly List<XfaComposedPage> _pages = new List<XfaComposedPage>();

    private int _areaCursor;
    private int _instancesOfCurrentArea;
    private int _contentAreaIndex;
    private double _penY;
    private bool _currentPageHasContent;

    private XfaPaginator(List<XfaPageArea> pageAreas)
    {
        _pageAreas = pageAreas;
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

        List<XfaPageArea> pageAreas = CollectPageAreas(root);
        if (pageAreas.Count == 0)
        {
            pageAreas.Add(DefaultPageArea());
        }

        XfaPaginator paginator = new XfaPaginator(pageAreas);
        paginator.NewPage();
        paginator.ComposeRoot(root);
        return paginator._pages;
    }

    private void ComposeRoot(XfaSubform root)
    {
        bool flowRoot = root.Layout is XfaLayout.TopToBottom or XfaLayout.Tb;

        foreach (XfaNode child in root.Children)
        {
            if (child is XfaPageSet or XfaPageArea or XfaContentArea
                || child.Presence is XfaPresence.Hidden or XfaPresence.Inactive)
            {
                continue;
            }

            if (flowRoot)
            {
                ComposeFlowedBlock(child);
            }
            else
            {
                ComposePositionedBlock(child);
            }
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

    private void ComposeFlowedBlock(XfaNode child)
    {
        if (child.BreakBefore is { } breakBefore)
        {
            Break(breakBefore);
        }

        (double marginTop, double marginBottom, _, _) = MarginsOf(child);
        double contentHeight = child.Height?.Points ?? MeasureHeight(child);
        double blockHeight = marginTop + contentHeight + marginBottom;

        (int pageIndex, double x, double y) = Place(blockHeight);

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
    }

    // Places a block of the given height into the current content area,
    // advancing to the next content area / page when it does not fit.
    private (int PageIndex, double X, double Y) Place(double height)
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
                return (_pages.Count - 1, x, y);
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

    // Walks the page areas per orderedOccurrence: each area is used up to its
    // MaxOccur count (-1 = unbounded), then the cursor advances. An exhausted
    // sequence reuses the final area so overflow always has a target.
    private XfaPageArea NextPageArea()
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

    // Collects the page areas of the first pageSet under the root, in document
    // order. (duplex/simplex pairing is handled in a later phase; the walk here
    // implements orderedOccurrence.)
    private static List<XfaPageArea> CollectPageAreas(XfaNode root)
    {
        List<XfaPageArea> areas = new List<XfaPageArea>();
        XfaPageSet? pageSet = FindFirstPageSet(root);
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
