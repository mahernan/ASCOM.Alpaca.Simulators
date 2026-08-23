## Camera Simulators

Normal simulation uses ImageSharp (https://github.com/SixLabors/ImageSharp Apache 2.0 licensed) to load the M42 image in a cross-platform way.

Replay supports either one binary P5 PGM file or a directory of P5 PGM files and returns unsigned sample values unchanged after each timed exposure. Directory entries are limited to `.pgm` files and sorted by filename with ordinal ordering. All files are decoded and validated on configuration and connection; width, height, and `maxval` must match. The camera reports those values and a monochrome sensor.

Replay supports full-frame acquisition only: `BinX=1`, `BinY=1`, `StartX=0`, `StartY=0`, `NumX=CameraXSize`, and `NumY=CameraYSize`. Other replay frame combinations are rejected when the exposure starts. A file is reserved after `StartExposure` validation, rendered when the existing exposure timer completes, and the directory index advances only after rendering succeeds. Reads do not advance it. Loop is the default end behavior; Stop at end rejects the next exposure after the final image. Disconnecting cancels an active replay selection, and the next connection reloads the directory at its first file.

### Manual directory replay

1. Open `http://localhost:32323/setup/v1/Camera/0/setup` (adjust the configured port and camera number if needed).
2. In **Simulated Image**, choose **PGM directory**, enter the server-side absolute directory path, choose **Loop** or **Stop at end**, save, and connect the camera.
3. Exercise the Alpaca API (transaction IDs may be changed as desired):

```sh
curl -X PUT -d 'Connected=true&ClientID=1&ClientTransactionID=1' http://localhost:32323/api/v1/camera/0/connected
curl -X PUT -d 'Duration=0.1&Light=true&ClientID=1&ClientTransactionID=2' http://localhost:32323/api/v1/camera/0/startexposure
curl 'http://localhost:32323/api/v1/camera/0/imageready?ClientID=1&ClientTransactionID=3'
curl 'http://localhost:32323/api/v1/camera/0/imagearray?ClientID=1&ClientTransactionID=4'
curl -H 'Accept: application/imagebytes' 'http://localhost:32323/api/v1/camera/0/imagearray?ClientID=1&ClientTransactionID=5' --output exposure.bin
curl -X PUT -d 'Duration=0.1&Light=true&ClientID=1&ClientTransactionID=6' http://localhost:32323/api/v1/camera/0/startexposure
```

Wait until `imageready` reports true before retrieving each image. Repeat the last three operations to observe ordinal progression and the configured loop or stop behavior.
