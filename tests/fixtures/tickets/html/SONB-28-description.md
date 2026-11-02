### Is there an existing issue for this?

- [x] I have searched the existing open and closed issues

### Current Behavior

The bitrate being shown on the media info modal is incorrect due to an incorrect usage of the `filesize` package.

The bits-per second value for audio & video returned from FFProbe is being passed as a value that is interpreted as a bytes count, resulting in an what often appears as an incorrect frame-rate units. k-bits vs kBytes per second.

Upstreamed from Radarr issue [11027](https://github.com/Radarr/Radarr/issues/11027) and PR [11087](https://github.com/Radarr/Radarr/pull/11087).

### Expected Behavior

The numeric value and accompanying units should be correct and mutally consistent.

### Steps To Reproduce

1. View the details of any given episode of any series for which a video is available.
2. Mouse-over the `Media Info` icon and view the `Audio Bitrate` and `Video Bitrate`
3. Click on the media info window and hover the mouse over the numeric bitrate values to see the raw bitrate in bps returned by FFProbe.
4. Check the math... the raw bitrate does not convert to the displayed kB/s or MB/s value displayed. As shown below, 3761890 bits per second is not equal to 3.8 MB/s - it's closer to 470.2 kB/s. (however, 3.8 kb/s would have been correct)

![Image](https://github.com/user-attachments/assets/67c4f545-76ef-4fae-b500-785ad9cbe162)

### Environment

```markdown
- OS: Windows
- Sonarr: v5-Develop build 
- Docker Install: No
- Using Reverse Proxy: No
- Browser: Firefox 138.0.4
- Database: SQLite 3.36.0
```

### What branch are you running?

Develop

### Trace Logs?

`VideoFileInfoReader.GetMediaInfo` doesn't trace log the output of `FFProbe.GetStreamJson` or the resulting `MediaInfoModel`.

### Anything else?

_No response_
