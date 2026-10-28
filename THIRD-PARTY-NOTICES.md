# Third-party notices

Chuvadi is licensed under the Apache License 2.0 (see `LICENSE`). Parts of it
are derived from the third-party software listed here, under the notices below.

## HarfBuzz

`Chuvadi.Pdf.Text.Shaping` (since 3.26.0) contains a C# port of parts of
HarfBuzz 15.0.0 (https://github.com/harfbuzz/harfbuzz): the OpenType layout
engine (GSUB/GPOS lookup application, the feature map, the normalizer, the
fallback mark positioning) and the Indic shaper. Its Unicode and Indic
property tables are generated from HarfBuzz's own data
(`tools/shaping-data`). The ported files say so in their headers.

    HarfBuzz is licensed under the so-called "Old MIT" license.  Details follow.
    For parts of HarfBuzz that are licensed under different licenses see individual
    files names COPYING in subdirectories where applicable.

    Copyright © 2010-2022  Google, Inc.
    Copyright © 2015-2020  Ebrahim Byagowi
    Copyright © 2019,2020  Facebook, Inc.
    Copyright © 2012,2015  Mozilla Foundation
    Copyright © 2011  Codethink Limited
    Copyright © 2008,2010  Nokia Corporation and/or its subsidiary(-ies)
    Copyright © 2009  Keith Stribley
    Copyright © 2011  Martin Hosken and SIL International
    Copyright © 2007  Chris Wilson
    Copyright © 2005,2006,2020,2021,2022,2023  Behdad Esfahbod
    Copyright © 2004,2007,2008,2009,2010,2013,2021,2022,2023  Red Hat, Inc.
    Copyright © 1998-2005  David Turner and Werner Lemberg
    Copyright © 2016  Igalia S.L.
    Copyright © 2022  Matthias Clasen
    Copyright © 2018,2021  Khaled Hosny
    Copyright © 2018,2019,2020  Adobe, Inc
    Copyright © 2013-2015  Alexei Podtelezhnikov

    For full copyright notices consult the individual files in the package.


    Permission is hereby granted, without written agreement and without
    license or royalty fees, to use, copy, modify, and distribute this
    software and its documentation for any purpose, provided that the
    above copyright notice and the following two paragraphs appear in
    all copies of this software.

    IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE TO ANY PARTY FOR
    DIRECT, INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES
    ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS DOCUMENTATION, EVEN
    IF THE COPYRIGHT HOLDER HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH
    DAMAGE.

    THE COPYRIGHT HOLDER SPECIFICALLY DISCLAIMS ANY WARRANTIES, INCLUDING,
    BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
    FITNESS FOR A PARTICULAR PURPOSE.  THE SOFTWARE PROVIDED HEREUNDER IS
    ON AN "AS IS" BASIS, AND THE COPYRIGHT HOLDER HAS NO OBLIGATION TO
    PROVIDE MAINTENANCE, SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.
