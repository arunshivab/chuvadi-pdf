// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.11.2 — Optional content groups
//        PDF 32000-1:2008 §8.11.4 — Optional content configuration
// PHASE: Phase 1.1.7 — Chuvadi.Pdf.Documents

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Reads optional content groups (layers) from a PDF document.
/// </summary>
public static class OptionalContentReader
{
    /// <summary>
    /// Returns every Optional Content Group declared in the document's
    /// /OCProperties/OCGs array, with visibility resolved from the default
    /// configuration (/OCProperties/D).
    /// </summary>
    /// <param name="document">The document to read.</param>
    /// <returns>Zero or more layers in declaration order.</returns>
    public static IReadOnlyList<OptionalContentGroup> GetGroups(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        PdfDictionary catalog = document.Catalog;
        PdfObjectStore store = document.Objects;

        if (!catalog.TryGetValue(PdfName.Intern("OCProperties"), out PdfPrimitive? ocPrim))
        {
            return Array.Empty<OptionalContentGroup>();
        }

        PdfDictionary? ocProps = store.ResolveAs<PdfDictionary>(ocPrim);
        if (ocProps is null)
        {
            return Array.Empty<OptionalContentGroup>();
        }

        // /OCGs — array of OCG dictionary references.
        PdfArray? ocgs = null;

        if (ocProps.TryGetValue(PdfName.Intern("OCGs"), out PdfPrimitive? ocgsPrim))
        {
            ocgs = store.ResolveAs<PdfArray>(ocgsPrim);
        }

        if (ocgs is null)
        {
            return Array.Empty<OptionalContentGroup>();
        }

        // Default config /D — drives initial visibility.
        HashSet<int> onSet = new HashSet<int>();
        HashSet<int> offSet = new HashSet<int>();
        HashSet<int> lockedSet = new HashSet<int>();
        string baseState = "ON";

        if (ocProps.TryGetValue(PdfName.Intern("D"), out PdfPrimitive? dPrim))
        {
            PdfDictionary? defaultConfig = store.ResolveAs<PdfDictionary>(dPrim);
            if (defaultConfig is not null)
            {
                CollectStateReferences(defaultConfig, store, PdfName.Intern("ON"), onSet);
                CollectStateReferences(defaultConfig, store, PdfName.Intern("OFF"), offSet);
                CollectStateReferences(defaultConfig, store, PdfName.Intern("Locked"), lockedSet);

                PdfName? bs = defaultConfig.GetName(PdfName.Intern("BaseState"));
                if (bs is not null)
                {
                    baseState = bs.Value;
                }
            }
        }

        List<OptionalContentGroup> result = new List<OptionalContentGroup>(ocgs.Count);

        for (int i = 0; i < ocgs.Count; i++)
        {
            PdfPrimitive raw = ocgs[i];
            int objectNum = (raw is PdfReference r) ? r.ObjectId.ObjectNumber : -1;

            PdfDictionary? ocg = store.ResolveAs<PdfDictionary>(raw);
            if (ocg is null)
            {
                continue;
            }

            string name = string.Empty;
            if (ocg.TryGetValue(PdfName.Intern("Name"), out PdfPrimitive? namePrim) &&
                namePrim is PdfString nameStr)
            {
                name = System.Text.Encoding.Latin1.GetString(nameStr.Bytes);
            }

            // Visibility resolution per §8.11.4.2:
            //   BaseState ON: visible unless listed in /OFF
            //   BaseState OFF: hidden unless listed in /ON
            //   BaseState Unchanged: prefer /ON over /OFF; otherwise visible
            bool visible;
            if (baseState == "OFF")
            {
                visible = onSet.Contains(objectNum);
            }
            else
            {
                visible = !offSet.Contains(objectNum);
                if (onSet.Contains(objectNum))
                {
                    visible = true;
                }
            }

            List<string> intents = new List<string>();

            if (ocg.TryGetValue(PdfName.Intern("Intent"), out PdfPrimitive? intentPrim))
            {
                if (intentPrim is PdfName intentName)
                {
                    intents.Add(intentName.Value);
                }
                else if (store.ResolveAs<PdfArray>(intentPrim) is PdfArray intentArr)
                {
                    for (int k = 0; k < intentArr.Count; k++)
                    {
                        if (intentArr[k] is PdfName iName)
                        {
                            intents.Add(iName.Value);
                        }
                    }
                }
            }

            result.Add(new OptionalContentGroup(name, visible, intents)
            {
                Id = objectNum,
                IsLocked = objectNum >= 0 && lockedSet.Contains(objectNum),
            });
        }

        return result;
    }

    /// <summary>
    /// Returns the human-readable name of the default OCG configuration
    /// (/OCProperties/D/Name), or null when none is set.
    /// </summary>
    public static string? GetDefaultConfigurationName(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!document.Catalog.TryGetValue(PdfName.Intern("OCProperties"), out PdfPrimitive? ocPrim))
        {
            return null;
        }

        PdfDictionary? ocProps = document.Objects.ResolveAs<PdfDictionary>(ocPrim);
        if (ocProps is null)
        {
            return null;
        }

        if (!ocProps.TryGetValue(PdfName.Intern("D"), out PdfPrimitive? dPrim))
        {
            return null;
        }

        PdfDictionary? d = document.Objects.ResolveAs<PdfDictionary>(dPrim);
        if (d is null)
        {
            return null;
        }

        if (d.TryGetValue(PdfName.Intern("Name"), out PdfPrimitive? namePrim) &&
            namePrim is PdfString nameStr)
        {
            return System.Text.Encoding.Latin1.GetString(nameStr.Bytes);
        }

        return null;
    }

    private static void CollectStateReferences(
        PdfDictionary config, PdfObjectStore store, PdfName key, HashSet<int> result)
    {
        if (!config.TryGetValue(key, out PdfPrimitive? prim))
        {
            return;
        }

        PdfArray? arr = store.ResolveAs<PdfArray>(prim);
        if (arr is null)
        {
            return;
        }

        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfReference r)
            {
                result.Add(r.ObjectId.ObjectNumber);
            }
        }
    }
}
