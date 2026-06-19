// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.3 — Chuvadi.Pdf.Redaction pattern validators
// Checksum validators used as PatternRule post-match predicates to reject false
// regex hits (e.g. a 12-digit string that is not a valid Aadhaar number).

using System;

namespace Chuvadi.Pdf.Redaction;

/// <summary>
/// Reusable checksum validators for <see cref="PatternRule"/> post-match
/// predicates. Each takes the matched text (formatting characters are ignored)
/// and returns <see langword="true"/> when the checksum is valid.
/// </summary>
public static class PatternValidators
{
    private static readonly int[,] VerhoeffD =
    {
        { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },
        { 1, 2, 3, 4, 0, 6, 7, 8, 9, 5 },
        { 2, 3, 4, 0, 1, 7, 8, 9, 5, 6 },
        { 3, 4, 0, 1, 2, 8, 9, 5, 6, 7 },
        { 4, 0, 1, 2, 3, 9, 5, 6, 7, 8 },
        { 5, 9, 8, 7, 6, 0, 4, 3, 2, 1 },
        { 6, 5, 9, 8, 7, 1, 0, 4, 3, 2 },
        { 7, 6, 5, 9, 8, 2, 1, 0, 4, 3 },
        { 8, 7, 6, 5, 9, 3, 2, 1, 0, 4 },
        { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 },
    };

    private static readonly int[,] VerhoeffP =
    {
        { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },
        { 1, 5, 7, 6, 2, 8, 3, 0, 9, 4 },
        { 5, 8, 0, 3, 7, 9, 6, 1, 4, 2 },
        { 8, 9, 1, 6, 0, 4, 3, 5, 2, 7 },
        { 9, 4, 5, 3, 1, 2, 6, 8, 7, 0 },
        { 4, 2, 8, 6, 5, 7, 3, 9, 0, 1 },
        { 2, 7, 9, 3, 8, 0, 6, 4, 1, 5 },
        { 7, 0, 4, 6, 9, 1, 3, 2, 5, 8 },
    };

    private static readonly int[] AbaWeights = { 3, 7, 1, 3, 7, 1, 3, 7, 1 };

    /// <summary>
    /// Validates a number with the Luhn (mod-10) checksum, ignoring spaces and
    /// dashes. Used for payment card numbers.
    /// </summary>
    /// <param name="value">The candidate text.</param>
    /// <returns><see langword="true"/> if the Luhn checksum is valid.</returns>
    public static bool Luhn(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string digits = DigitsOnly(value);
        if (digits.Length == 0)
        {
            return false;
        }

        return LuhnDigits(digits);
    }

    /// <summary>
    /// Validates a number with the Verhoeff checksum, ignoring spaces. Used for
    /// Indian Aadhaar numbers (the 12-digit length is enforced by the pattern).
    /// </summary>
    /// <param name="value">The candidate text.</param>
    /// <returns><see langword="true"/> if the Verhoeff checksum is valid.</returns>
    public static bool Verhoeff(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string digits = DigitsOnly(value);
        if (digits.Length == 0)
        {
            return false;
        }

        int c = 0;
        for (int i = 0; i < digits.Length; i++)
        {
            int digit = digits[digits.Length - 1 - i] - '0';
            c = VerhoeffD[c, VerhoeffP[i % 8, digit]];
        }

        return c == 0;
    }

    /// <summary>
    /// Validates an IBAN with the ISO 13616 mod-97 checksum, ignoring spaces.
    /// </summary>
    /// <param name="value">The candidate text.</param>
    /// <returns><see langword="true"/> if the IBAN checksum is valid.</returns>
    public static bool Iban(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string compact = Compact(value);
        if (compact.Length < 15 || compact.Length > 34)
        {
            return false;
        }

        string rearranged = compact.Substring(4) + compact.Substring(0, 4);
        int remainder = 0;
        foreach (char ch in rearranged)
        {
            int code;
            if (ch >= '0' && ch <= '9')
            {
                code = ch - '0';
            }
            else if (ch >= 'A' && ch <= 'Z')
            {
                code = ch - 'A' + 10;
            }
            else
            {
                return false;
            }

            // Fold each base-10 / base-36 piece into the running mod-97 value.
            remainder = code > 9 ? ((remainder * 100) + code) % 97 : ((remainder * 10) + code) % 97;
        }

        return remainder == 1;
    }

    /// <summary>
    /// Validates a 9-digit US ABA routing number with its weighted (3-7-1)
    /// checksum, ignoring spaces and dashes.
    /// </summary>
    /// <param name="value">The candidate text.</param>
    /// <returns><see langword="true"/> if the routing checksum is valid.</returns>
    public static bool AbaRouting(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string digits = DigitsOnly(value);
        if (digits.Length != 9)
        {
            return false;
        }

        int sum = 0;
        for (int i = 0; i < 9; i++)
        {
            sum += (digits[i] - '0') * AbaWeights[i];
        }

        return sum % 10 == 0;
    }

    /// <summary>
    /// Validates a 10-digit US National Provider Identifier (NPI) using the Luhn
    /// checksum over the "80840" prefix, ignoring spaces and dashes.
    /// </summary>
    /// <param name="value">The candidate text.</param>
    /// <returns><see langword="true"/> if the NPI checksum is valid.</returns>
    public static bool Npi(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string digits = DigitsOnly(value);
        if (digits.Length != 10)
        {
            return false;
        }

        return LuhnDigits("80840" + digits);
    }

    private static bool LuhnDigits(string digits)
    {
        int sum = 0;
        bool alternate = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (alternate)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    private static string DigitsOnly(string value)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(value.Length);
        foreach (char ch in value)
        {
            if (ch >= '0' && ch <= '9')
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static string Compact(string value)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(value.Length);
        foreach (char ch in value)
        {
            if (ch != ' ' && ch != '\t')
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }

        return sb.ToString();
    }
}
