# MiTek paperwork and MPS files - which way up

**Bird's eye.** MiTek paperwork and MPS files show a truss or panel looking **down on its front
face**, with that face up on the table.

- **Confirmed by the MiTek rep** (email, 23 September 2026): the default setting is Bird's Eye
  view. A user *can* switch it to Worm's Eye, but the setting is hidden away and very unlikely to
  have been changed. If a job's plates look mirrored, that setting is the first thing to ask about.
- **Matches the file spec** (MiTek Truss/Panel Construction File, MPS V4.4):
  - §3.6 Layer Code: layer 0 is the **front face** of the main members, called "the upper, out of
    plane, face"; layers 1 and 2 "sit on the upper face". The back face is layer -6.
  - Joint record: "+Z axis is out of plane **in front of** frame/truss, Z=0 is face of truss or
    frame and its thickness is in the **-Z direction**" - Z points at the viewer.
  - §16 Plate Record: each plate has a front layer, normally **L1** (on top), and a back layer,
    normally **L-6** (underneath). The spec's worked example says -7 instead; either way, the back.

## On the table

- The X,Y plate positions are for the **top** plate, seen from above.
- The **bottom** plate at the same joint sits directly underneath at the same X,Y. It is not given
  separately mirrored, so seen from underneath it appears mirrored.
- Each plate outline has 5 points, not 4: the extra 4th point shows which way the slots run, so a
  square plate (e.g. 100x100) still has a known orientation.

The app does not read MPS files yet; this is here so the answer is not lost.
