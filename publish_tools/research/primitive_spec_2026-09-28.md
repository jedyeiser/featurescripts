# Export Primitive -- user brief (2026-09-28, verbatim)

When I started designing skis (almost 20 years ago), My manager forced me to make 'primative drawings' of each ski. Basically, the footprint, profile, rocker profile and associated data of the ski. While I've continuied thinking about skis like this, even incorporating the approach into some design tools we've moved away from documenting designs this way, and I think we need to get back to it. Not only does it serve as a really convienient basis for comparison between skis, but it serves as a great place to communicate some 'overall' information about the ski - information that really IS ABOUT the geometry of the ski.

We currently often create cross section drawings of full skis (could be an assembly if this was easier to do, I understood how to do it, or we could write an API tool to help us here) from a multibody part (a composite part).

I'm working towars a more programatic/parametric flow in Onshape. Think about part studios as functions. It wouldn't be the worst idea to have standard drawings for different kinds of output part studios for us to document and inspect our work.

I'd like to combine all of this and integrate it into our publish and drawing work. We will add to this as we go along. I'm going to focus on the primitive for now, you should do research on cross section and provide your suggestions to me. Note that our current process is brittle and it may be difficult for you to find a working drawing. If that's the case, I will spend a few minuites finding good examples to provide you with.

```
Export Primiative. We will either need a larger drawing, or to use multiple sheets.
    Create tables
    MBD?

    Required Inputs:
        volume (part)
        fcp (point, mate connector)
        acp (point, mate connector)
        baseline_from (enum - {VOLUME, INPUT})
            Baseline wire when INPUT
        footprint_from (enum - {VOLUME, INPUT})
            Footprint wire(s) when INPUT
        datum (mate connector, can be created)
        radius_between {CONTACTS, WIDEST, INFLECTION}
        any unwrapping reference points needed. In general, we can assume MRS for allignment points.
        table specific user input
    Optional inputs
        sw_rout_surface
        sw_rout_start
        sw_rout_stop
        target EI
        ISO/bounding box information (Minimum thickness, distance from MP)

    Beyond tables/calculations, the feature should produce one composite with the following wires (and optionally surfaces, described below) and points.
        from top to bottom
        1. Baseline
        2. Profile view of volume
        3. unwrapped footprint
        4. radius progression "plot" (10mm - 1m radius. Positive -> sidecut. Negative -> taper (or tip/tail).
            Need to provide a reference line (0m
            Remember, inflection points can be between edges, or within an edge.
            Generally speaking, we want the inflection point closest to the widest point, on the side towards MRS
            If curvature profile is continuous, connect radius plot.
            arcs map to straight curvature/lines

{CAN/SHOULD WE CREATE A CUSTOM ONSHAPE DOCUMENT TYPE WITH GIVEN DATA FIELDS? CAN WE UPDATE THESE WITH FEATURESCRIPT? DO THINGS CHANGE IF WE CREATE A CUSTOM PART STUDIO TYPE (PRIMATIVE, DESIGN MASTER, PARTS). I don't love having to create/enforce document/part studio types, but if it helps, it may be worthwhile. Can you spin up an agent to do some research and give me a concise and complete .md file with imagery to ground decisions?}

In general, when we talk about profiles, we mean geometry in the XZ plane. Peripheries are in 'width' surface.
Create intersection wire(s) with front (or specified middle plane). Divide into top, bottom profiles.
TABLE 1: Theoretical Scalefactors
    for each length, show the length of the described region in a table. One column for Bottom, one column for Top. One column for scalefactor (Top/Bottom) in %
    a. Tip Length
        Tip is the region between FCP and the tip of the ski. A line from MRS (halfway between ACP and FCP) to FCP points towards the tip.
        Note that we need to take wire normals into account here.
        Because we have 'upwards' curvature, we expect the top length to be shorter than the bottom length.
    b. Running surface length
        length of the curve between FCP and ACP. Because the top of the ski is curved, we expect the top length to be SLIGHTLY longer here. We often ignore this difference because its so small.
    c. Tail length.

Table 2: Metadata
    RSL
    Dimensions
    Average radius
    Natural radius widest
    Natural radius inflection
    Taper angle widest
        angle between centerline and line that connects widest points
    Taper angle inflection
        angle between centerline and line that connects inflection points
    Theoretical deflection (mm/30kg)
    Theoretical stiffness (lb/in)

Table 3: Key locations (x on volume, s (distance along bottom wire - wich is basically ref_wire in many of our models)
    FCP, ACP, MRS, MP (can be multiple - snowboards), XS1, XS2, TIP, TAIL.
    For each point:
        [x, y, z], [s, w, h]

Table 4: SW Rout
    SW Rout Angle
    Dist_above_base
    Step-in
    start, stop (x, s)

Table 5: Baseline
    Tip Block - written in or from selected part name
    Tail Block - written in or from selected part name
    Tip_height
    FCPh
    FRCP
    FRCPl
    FB_Roll (Distance between min and inflection)
    MCh
    MCl (x, s)
    AB_Roll
    ARCPl
    ARCP
    ACPh
    Tail_height

Data Table (only within the RSL)
x, s, y, ski_width, z, ski_thck, baseline_height, Radius
for specified number of points between FCP and ACP (inclusive), optionally forcing XS1, MRS, XS2 if they don't pop up in our N samples.
```
