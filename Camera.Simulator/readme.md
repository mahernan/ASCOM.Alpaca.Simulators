## Camera Simulators

Normal simulation uses ImageSharp (https://github.com/SixLabors/ImageSharp Apache 2.0 licensed) to load the M42 image in a cross-platform way.

Replay image mode accepts one binary P5 PGM file and returns its unsigned sample values unchanged after each timed exposure. In this mode the camera reports the PGM width, height, and `maxval`, and its sensor type is monochrome. This first milestone supports full-frame acquisition only: `BinX=1`, `BinY=1`, `StartX=0`, `StartY=0`, `NumX=CameraXSize`, and `NumY=CameraYSize`. Other replay frame combinations are rejected when the exposure starts. Directory sequencing is not implemented.
