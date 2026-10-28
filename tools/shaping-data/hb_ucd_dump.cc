// Dumps the Unicode and Indic shaping properties that HarfBuzz uses, one line
// per code point, so tools/shaping-data/gen_shaping_tables.py can generate
// src/Chuvadi.Pdf.Text.Shaping/Unicode/UnicodeTables.g.cs from exactly the
// data HarfBuzz shapes with.
//
// Build against a HarfBuzz source checkout (the amalgamated harfbuzz.cc):
//
//   g++ -O1 -std=c++17 -w -I <harfbuzz>/src -DHB_NO_MT -o hb_ucd_dump hb_ucd_dump.cc -lm
//   ./hb_ucd_dump            (writes ucd.tsv in the current directory)
//
// Columns (tab separated): code point (hex), general category (HarfBuzz
// enum order), canonical combining class, HarfBuzz modified combining class,
// decomposition a (hex), decomposition b (hex), has decomposition (0/1),
// recomposes to the code point (0/1), Indic category | position << 8,
// default ignorable (0/1).

#include "harfbuzz.cc"
#include <cstdio>

extern uint16_t hb_indic_get_categories (hb_codepoint_t u);

int main ()
{
  hb_unicode_funcs_t *uf = hb_unicode_funcs_get_default ();
  FILE *f = fopen ("ucd.tsv", "w");
  for (hb_codepoint_t u = 0; u <= 0x10FFFF; u++)
  {
    unsigned gc = uf->general_category (u);
    unsigned ccc = uf->combining_class (u);
    unsigned mcc = uf->modified_combining_class (u);
    hb_codepoint_t a = 0, b = 0;
    bool d = uf->decompose (u, &a, &b);
    unsigned comp = 0;
    if (d && b)
    {
      hb_codepoint_t ab = 0;
      if (uf->compose (a, b, &ab) && ab == u)
        comp = 1;
    }
    unsigned ind = hb_indic_get_categories (u);
    unsigned di = uf->is_default_ignorable (u);
    fprintf (f, "%X\t%u\t%u\t%u\t%X\t%X\t%u\t%u\t%u\t%u\n",
             u, gc, ccc, mcc, d ? a : 0, d ? b : 0, (unsigned) d, comp, ind, di);
  }
  fclose (f);
  return 0;
}
