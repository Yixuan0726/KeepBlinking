# Care Station FILTER art source

`FILTER Level 1` no longer comes from the legacy SVG generator in this folder.
Its sole approved master is:

`Approved/Filter_Level1_Final_Reference.png`

`Approved/build_filter_l1_runtime_art.py` reads that exact master and creates
the semantic transparent runtime layers. Do not regenerate L1 with
`generate_filter_art.js`; the generator is now restricted to the unchanged
legacy L2/L3 resources.

`generate_filter_art.js` emits only the L2/L3 SVG layers beside the script and
rasterizes the same vectors to 1024 x 1024 transparent PNG files under:

`Assets/KeepBlinking/Art/CareStation/Filter/`

All layers share a common coordinate system and bottom-center pivot. The art
uses flat colors only: no gradients, glow, bloom, paper texture, global noise,
or procedural aging filters. Uneven silhouettes, broken secondary lines, and
non-mirrored details are authored directly in the vector geometry.

The raster step uses the bundled local `sharp` package and introduces no Unity
runtime dependency or network download.
