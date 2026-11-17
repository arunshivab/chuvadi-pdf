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

    // ISO 3166-1 alpha-2 country codes, and XK (Kosovo), used in BICs.
    private const string CountryCodes =
        "AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE "
        + "BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ CA CC CD "
        + "CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM "
        + "DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR GA GB GD GE GF "
        + "GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU "
        + "ID IE IL IM IN IO IQ IR IS IT JE JM JO JP KE KG KH KI KM KN "
        + "KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME "
        + "MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ NA "
        + "NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM "
        + "PN PR PS PT PW PY QA RE RO RS RU RW SA SB SC SD SE SG SH SI "
        + "SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK "
        + "TL TM TN TO TR TT TV TW TZ UA UG UM US UY UZ VA VC VE VG VI "
        + "VN VU WF WS YE YT ZA ZM ZW "
        + "XK ";

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

    /// <summary>
    /// Validates the shape of a SWIFT / BIC code (3.35.0): 8 or 11
    /// characters, four letters, then an ISO 3166 country code (or XK), then
    /// letters or digits. Spaces are ignored.
    /// </summary>
    /// <param name="value">The candidate text.</param>
    /// <returns><see langword="true"/> if the code has a valid country code.</returns>
    public static bool Bic(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string compact = Compact(value);
        if (compact.Length != 8 && compact.Length != 11)
        {
            return false;
        }

        for (int i = 0; i < compact.Length; i++)
        {
            char ch = compact[i];
            bool letter = ch >= 'A' && ch <= 'Z';
            bool digit = ch >= '0' && ch <= '9';
            if ((i < 6 && !letter) || (!letter && !digit))
            {
                return false;
            }
        }

        string country = compact.Substring(4, 2);
        return CountryCodes.Contains(country + " ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates a US Individual Taxpayer Identification Number (3.35.0):
    /// nine digits starting with 9, whose fourth and fifth digits are in
    /// 50–65, 70–88, 90–92 or 94–99 (the ranges the IRS issues). Spaces and
    /// dashes are ignored.
    /// </summary>
    /// <param name="value">The candidate text.</param>
    /// <returns><see langword="true"/> if the number can be an ITIN.</returns>
    public static bool Itin(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string digits = DigitsOnly(value);
        if (digits.Length != 9 || digits[0] != '9')
        {
            return false;
        }

        int group = ((digits[3] - '0') * 10) + (digits[4] - '0');
        return (group >= 50 && group <= 65)
            || (group >= 70 && group <= 88)
            || (group >= 90 && group <= 92)
            || (group >= 94 && group <= 99);
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
