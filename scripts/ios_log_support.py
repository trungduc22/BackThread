"""Bounded filtering of iOS syslog relay records, which may use NUL or LF terminators."""
from collections import deque
import re


class BackThreadLogs:
    def __init__(self, capacity=128, nul_framed=False):
        if capacity<1:raise ValueError('Positive capacity required')
        self.pending=b''
        self.lines=deque(maxlen=capacity)
        self.entry_markers=deque(maxlen=16)
        self.matches=0
        self.nul_framed=nul_framed

    def record(self, data):
        if b'backthread' not in data.lower():return
        line=data.decode(errors='replace')[:2048]
        # Keep app-container/job identifiers out of sharable diagnostic reports.
        line=re.sub(r'\b[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\b','<uuid>',line)
        self.lines.append(line);self.matches+=1
        if 'BackThread.Probe main-entered' in line or 'BackThread.Probe UIKit-ready' in line:
            self.entry_markers.append(line)

    def feed(self, data):
        # Native syslog relay uses NUL records; embedded LF fields belong to the same event.
        incoming=data if self.nul_framed else data.replace(b'\0',b'\n')
        parts=(self.pending+incoming).split(b'\0' if self.nul_framed else b'\n')
        self.pending=parts.pop()[-8192:]
        for part in parts:self.record(part)

    def finish(self):
        if self.pending:self.record(self.pending);self.pending=b''

    def report(self):
        return dict(backThreadLines=list(self.lines),entryMarkers=list(self.entry_markers),
                    matchingLinesObserved=self.matches,truncatedMatches=max(0,self.matches-len(self.lines)),
                    unrelatedLogContentStored=False)
