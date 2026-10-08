# Speedtest Backstage

A single portable native Windows GUI for ConnectWise ScreenConnect Backstage. It wraps the bundled official Ookla Speedtest CLI, requires no installer or Python runtime on the endpoint, and uses WinForms rather than Tkinter so its fixed Backstage window repaints reliably.

## Build

Run `build.bat` on the build machine. The only output is:

* `dist\SpeedtestPortable.exe`

Copy that one file to ScreenConnect Backstage.

## Usage

Choose the server, repeat count, interval, and optional duration in the fixed Backstage window, then select **Test starten**. A duration of 60 minutes with a five-minute interval runs immediately and then every five minutes (13 runs total); leave duration at zero to use the repeat count. The built-in graph displays one selected metric at a time and begins only after three successful runs; the table below it keeps the latest results readable in the fixed window.

The executable stores its local history beside itself as `speedtest-history.csv`, including download/upload Mbps, ping, jitter, packet loss, ISP, server, timestamp, and the result URL.
