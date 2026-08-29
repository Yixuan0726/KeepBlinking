# FILTER static visual study v2

> Archived/rejected study. Its Level 1 concept is obsolete and must not be
> rendered, imported, or used as a runtime fallback. The only approved Level 1
> source is `../Approved/Filter_Level1_Final_Reference.png`.

This folder contains the reference-led, manually authored FILTER review art for
the first approval stage only.

The supplied `FILTER LV.1 → LV.2 → LV.3` illustration was inspected before the
drawings were authored. It drives the shared cylindrical silhouette, patched
Level 1 construction, rebuilt olive Level 2 chassis, brick-red Level 3 chassis,
filter-bed count, crank placement, integrated Level 3 gauge and brush, narrow
care-fluid flow, and unified front FILTER function badge.

No source pixels from the reference are embedded, cropped, traced by an
automatic tool, upscaled, or used as a runtime texture. The SVGs use manually
arranged irregular paths, flat fills, varied dark-brown line weights, and a
single broad hard-edged grounding shadow per machine. They contain no gradient,
glow, bloom, paper texture, global noise, or procedural aging filter.

Run `render_filter_concept_v2.js` with the bundled local `sharp` package to
produce only:

- `Logs/CareStationFilterConceptV2/Filter_L1_L2_L3_Comparison_v2.png`
- `Logs/CareStationFilterConceptV2/Filter_Station_Mobile_Triptych_v2.png`
- `Logs/CareStationFilterConceptV2/Filter_ConceptV2_Metrics.json`

The mobile triptych uses three exact 1320 × 2868 panels and the current 284 ×
284 FILTER art slot so that the scale is not exaggerated. The other Station
elements are low-contrast graybox context, not new production art.

This script never reads or writes the current 22 runtime PNGs, their SVG source,
the Sprite Catalog, Unity importer, animation layers, or gameplay code. No flow
frames or runtime replacements are produced until the static study is approved.
