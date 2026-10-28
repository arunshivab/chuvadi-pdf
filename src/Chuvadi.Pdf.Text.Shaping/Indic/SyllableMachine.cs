// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// A small longest-match scanner over category sequences, used to run the
// Indic syllable grammar of HarfBuzz's hb-ot-shaper-indic-machine.rl (a
// Ragel scanner: longest match wins, earlier pattern wins ties); see
// THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Text.Shaping.Indic;

/// <summary>A regular expression over small-integer categories, compiled to an NFA.</summary>
internal sealed class CatPattern
{
    private readonly Func<Nfa, (int Start, int End)> _build;

    private CatPattern(Func<Nfa, (int Start, int End)> build)
    {
        _build = build;
    }

    internal static CatPattern Cat(params int[] categories)
    {
        HashSet<int> set = new HashSet<int>(categories);
        return new CatPattern(n =>
        {
            int s = n.NewState();
            int e = n.NewState();
            n.AddEdge(s, e, set);
            return (s, e);
        });
    }

    internal static CatPattern Any() => new CatPattern(n =>
    {
        int s = n.NewState();
        int e = n.NewState();
        n.AddEdge(s, e, null);
        return (s, e);
    });

    internal static CatPattern Seq(params CatPattern[] parts) => new CatPattern(n =>
    {
        int s = n.NewState();
        int cur = s;
        foreach (CatPattern p in parts)
        {
            (int ps, int pe) = p._build(n);
            n.AddEpsilon(cur, ps);
            cur = pe;
        }

        return (s, cur);
    });

    internal static CatPattern Alt(params CatPattern[] parts) => new CatPattern(n =>
    {
        int s = n.NewState();
        int e = n.NewState();
        foreach (CatPattern p in parts)
        {
            (int ps, int pe) = p._build(n);
            n.AddEpsilon(s, ps);
            n.AddEpsilon(pe, e);
        }

        return (s, e);
    });

    internal CatPattern Opt() => Alt(this, Seq());

    internal CatPattern Star() => new CatPattern(n =>
    {
        int s = n.NewState();
        (int ps, int pe) = _build(n);
        n.AddEpsilon(s, ps);
        n.AddEpsilon(pe, s);
        return (s, s);
    });

    internal Nfa Compile()
    {
        Nfa n = new Nfa();
        (int s, int e) = _build(n);
        n.Start = s;
        n.Accept = e;
        return n;
    }
}

/// <summary>A Thompson NFA; <see cref="ToDfa"/> determinizes it.</summary>
internal sealed class Nfa
{
    private readonly List<List<int>> _epsilon = new List<List<int>>();
    private readonly List<List<(HashSet<int>? Set, int To)>> _edges = new List<List<(HashSet<int>?, int)>>();

    internal int Start { get; set; }

    internal int Accept { get; set; }

    internal int NewState()
    {
        _epsilon.Add(new List<int>());
        _edges.Add(new List<(HashSet<int>?, int)>());
        return _epsilon.Count - 1;
    }

    internal void AddEpsilon(int from, int to) => _epsilon[from].Add(to);

    internal void AddEdge(int from, int to, HashSet<int>? set) => _edges[from].Add((set, to));

    /// <summary>Determinizes the automaton (subset construction) over categories 0–63.</summary>
    internal Dfa ToDfa()
    {
        List<HashSet<int>> sets = new List<HashSet<int>>();
        Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.Ordinal);
        List<int[]> transitions = new List<int[]>();
        List<bool> accepting = new List<bool>();

        int Add(HashSet<int> set)
        {
            List<int> sorted = new List<int>(set);
            sorted.Sort();
            string key = string.Join(",", sorted);
            if (index.TryGetValue(key, out int existing))
            {
                return existing;
            }

            index[key] = sets.Count;
            sets.Add(set);
            int[] row = new int[Dfa.Alphabet];
            Array.Fill(row, -1);
            transitions.Add(row);
            accepting.Add(set.Contains(Accept));
            return sets.Count - 1;
        }

        Add(Closure(new HashSet<int> { Start }));
        for (int d = 0; d < sets.Count; d++)
        {
            for (int c = 0; c < Dfa.Alphabet; c++)
            {
                HashSet<int> next = new HashSet<int>();
                foreach (int s in sets[d])
                {
                    foreach ((HashSet<int>? set, int to) in _edges[s])
                    {
                        if (set is null || set.Contains(c))
                        {
                            next.Add(to);
                        }
                    }
                }

                if (next.Count > 0)
                {
                    transitions[d][c] = Add(Closure(next));
                }
            }
        }

        return new Dfa(transitions.ToArray(), accepting.ToArray());
    }

    private HashSet<int> Closure(HashSet<int> states)
    {
        Stack<int> stack = new Stack<int>(states);
        while (stack.Count > 0)
        {
            int s = stack.Pop();
            foreach (int t in _epsilon[s])
            {
                if (states.Add(t))
                {
                    stack.Push(t);
                }
            }
        }

        return states;
    }
}

/// <summary>A deterministic automaton; <see cref="LongestMatch"/> runs it.</summary>
internal sealed class Dfa
{
    internal const int Alphabet = 64;

    private readonly int[][] _next;
    private readonly bool[] _accepting;

    internal Dfa(int[][] next, bool[] accepting)
    {
        _next = next;
        _accepting = accepting;
    }

    /// <summary>Returns the length of the longest match at <paramref name="start"/>, or -1.</summary>
    internal int LongestMatch(IReadOnlyList<int> input, int start)
    {
        int state = 0;
        int best = _accepting[0] ? 0 : -1;
        for (int p = start; p < input.Count; p++)
        {
            int c = input[p];
            state = _next[state][c is >= 0 and < Alphabet ? c : Alphabet - 1];
            if (state < 0)
            {
                break;
            }

            if (_accepting[state])
            {
                best = p - start + 1;
            }
        }

        return best;
    }
}
