"""Portable offline evidence verification. Python standard library only; no network or audio."""
import array
import runpy
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parent
def require(condition,message):
    if not condition: raise ValueError(message)
def digest(data): return hashlib.sha256(data).hexdigest()
def read_json(name): return json.loads((ROOT/name).read_bytes())

class MessagePack:
    """Small bounded reader for captured maps/scalars/binary; never executes data."""

    def __init__(self, data):
        require(len(data) <= 8 * 1024 * 1024, "Oversized MessagePack artifact")
        self.data = data
        self.position = 0

    def take(self, count):
        require(0 <= count <= len(self.data) - self.position, "Truncated MessagePack artifact")
        result = self.data[self.position : self.position + count]
        self.position += count
        return result

    def number(self, format_string):
        return struct.unpack(format_string, self.take(struct.calcsize(format_string)))[0]

    def read(self, depth=0):
        require(depth < 32, "MessagePack depth limit")
        code = self.take(1)[0]
        if code <= 0x7F:
            return code
        if code >= 0xE0:
            return code - 256
        if 0xA0 <= code <= 0xBF:
            return self.take(code & 31).decode("utf-8")
        if 0x80 <= code <= 0x8F:
            return self.mapping(code & 15, depth)
        if 0x90 <= code <= 0x9F:
            return self.array(code & 15, depth)
        if code in (0xC0, 0xC2, 0xC3):
            return {0xC0: None, 0xC2: False, 0xC3: True}[code]
        if code in (0xC4, 0xC5, 0xC6):
            return self.take(self.number({0xC4: ">B", 0xC5: ">H", 0xC6: ">I"}[code]))
        if code in (0xD9, 0xDA, 0xDB):
            return self.take(self.number({0xD9: ">B", 0xDA: ">H", 0xDB: ">I"}[code])).decode("utf-8")
        if code in (0xDC, 0xDD):
            return self.array(self.number({0xDC: ">H", 0xDD: ">I"}[code]), depth)
        if code in (0xDE, 0xDF):
            return self.mapping(self.number({0xDE: ">H", 0xDF: ">I"}[code]), depth)
        formats = {0xCA: ">f", 0xCB: ">d", 0xCC: ">B", 0xCD: ">H", 0xCE: ">I", 0xCF: ">Q",
                   0xD0: ">b", 0xD1: ">h", 0xD2: ">i", 0xD3: ">q"}
        require(code in formats, "Unsupported MessagePack type")
        return self.number(formats[code])

    def mapping(self, count, depth):
        require(count <= 4096, "MessagePack map limit")
        result = {}
        for _ in range(count):
            key = self.read(depth + 1)
            require(isinstance(key, str) and key not in result, "Invalid/duplicate MessagePack key")
            result[key] = self.read(depth + 1)
        return result

    def array(self, count, depth):
        require(count <= 4096, "MessagePack array limit")
        return [self.read(depth + 1) for _ in range(count)]


def decode(path):
    reader = MessagePack(path.read_bytes())
    value = reader.read()
    require(reader.position == len(reader.data), "Trailing MessagePack bytes: " + path.name)
    require(isinstance(value, dict), "Expected MessagePack map: " + path.name)
    return value



def main():
    manifest=read_json('manifest.json')
    listed=set()
    for entry in manifest['files']:
        name=entry['name']
        relative=Path(name)
        require(not relative.is_absolute() and '..' not in relative.parts,'Unsafe manifest path')
        require(name not in listed,'Duplicate manifest entry')
        listed.add(name)
        data=(ROOT/relative).read_bytes()
        require(len(data)==entry['bytes'] and digest(data)==entry['sha256'],'Manifest mismatch: '+name)
        if entry['relationship'].startswith('exact original bytes'):
            require(digest(data)==entry['originalCaptureSha256'],'Original identity mismatch: '+name)
        if entry['relationship'].startswith('exact original replay'):
            require(digest(data)==entry['originalReplaySha256'],'Original replay identity mismatch: '+name)
        if relative.suffix in ('.json','.txt'):
            text=data.decode('utf-8-sig')
            require(not re.search(r'(?i)(?<![A-Z0-9])[A-Z]:[\\/]|/(?:Users|home)/|\\Users\\|\.codex|task-state',text),'Private filesystem context: '+name)
    actual={p.relative_to(ROOT).as_posix() for p in ROOT.rglob('*') if p.is_file()}
    require(actual==listed|{'manifest.json'},'Unexpected or missing publication files')
    request=read_json('capture/request-summary.sanitized.json')
    summary=read_json('capture/capture-summary.original.json')
    comparison=read_json('capture/pcm-comparison.json')
    reads=read_json('capture/provider-reads.json')
    positions=read_json('capture/device-positions.json')
    require(request['headers']=={'Authorization':'[REDACTED]','model':'s2.1-pro-free'},'Unsafe model/header policy')
    require(request['retryCount']==0 and request['fallback'] is False,'Retry/fallback mismatch')
    incoming=[decode(p) for p in sorted((ROOT/'capture').glob('incoming-*.msgpack'))]
    outgoing=[decode(p) for p in sorted((ROOT/'capture').glob('outgoing-*.msgpack'))]
    require(len(incoming)==13 and len(outgoing)==12,'Raw message count')
    require(incoming[-1]['event']=='finish' and incoming[-1]['reason']=='stop','Unsuccessful finish')
    audio=b''.join(item['audio'] for item in incoming if item['event']=='audio')
    require(sum(item['event']=='audio' for item in incoming)==12,'Audio response count')
    require(len(audio)==6053526 and digest(audio)==comparison['UpstreamSha256'],'Reconstructed upstream mismatch')
    require(digest(audio)=='3a7783c5ccb27cbebf977a5c75c6f954b1ec6bf50359882b808d584feb32397f','Unexpected sample')
    events=['start']+[item for _ in range(5) for item in ('text','flush')]+['stop']
    require([item['event']for item in outgoing]==events,'Outgoing sequence')
    text=''.join(item['text']for item in outgoing if item['event']=='text')
    require(text==request['text'] and len(text)==request['textUtf16Length']==950,'Input text mismatch')
    require(digest(text.encode('utf-8'))==request['textSha256'],'Input hash mismatch')
    body=outgoing[0]['request'];settings=request['requestSnapshot']
    for wire,setting in [('reference_id','VoiceId'),('format','AudioFormat'),('temperature','Temperature'),('top_p','TopP'),('normalize','Normalize'),('latency','Latency'),('condition_on_previous_chunks','ConditionOnPreviousChunks')]:
        require(body[wire]==settings[setting],'Request snapshot mismatch: '+wire)
    for wire,setting in [('speed','Speed'),('volume','Volume'),('normalize_loudness','NormalizeLoudness')]:
        require(body['prosody'][wire]==settings[setting],'Prosody mismatch: '+wire)
    require(body['format']=='pcm' and body['sample_rate']==44100 and 'mp3_bitrate' not in body,'PCM format mismatch')
    require(settings['SelectedModel']=='s2.1-pro-free' and re.fullmatch('[0-9a-f]{32}',settings['VoiceId']) is not None,'Model/voice identity')
    rendered=(ROOT/'capture/rendered-audio.pcm').read_bytes()
    require(rendered[:len(audio)]==audio and not any(rendered[len(audio):]),'Rendered prefix/tail mismatch')
    cursor=0;real_parts=[];padding=[];last_tail=0
    for sequence,read in enumerate(reads):
        require(read['Sequence']==sequence and read['RenderedByteOffset']==cursor==read['DeviceSourceCursorBefore'],'Read cursor mismatch')
        count=read['ReturnedBytes'];real=read['RealBytes'];pad=read['PaddingBytes']
        require(count==read['RequestedBytes']==4410 and real+pad==count and real>=0 and pad>=0,'Read span mismatch')
        real_parts.append(rendered[cursor:cursor+real])
        if real:last_tail=cursor+real
        require(read['LastRealPcmEnd']==last_tail,'Real tail cursor mismatch')
        if pad:
            require(not any(rendered[cursor+real:cursor+count]),'Nonzero padding')
            padding.append((cursor+real,pad))
        cursor+=count
    require(cursor==len(rendered) and b''.join(real_parts)==audio,'Real-span reconstruction mismatch')
    require(all(start>=len(audio)for start,pad in padding),'Speech-time padding')
    require(len(padding)==2 and sum(pad for start,pad in padding)==5814,'Padding count mismatch')
    require(max(item['Bytes']for item in positions)>=last_tail==len(audio),'Device drain mismatch')
    require(summary['outcome']=='completed' and summary['verification']['Passed'] and not summary['anyArtifactRedacted'],'Original capture failed')
    original={item['Name']:item for item in summary['artifacts']}
    require(original['raw-audio.pcm']['OriginalSha256']==digest(audio),'Omitted upstream identity mismatch')
    require(original['request-summary.json']['OriginalSha256']==request['publicationProvenance']['originalCaptureSha256'],'Sanitized request provenance mismatch')
    require(not (ROOT/'capture/raw-audio.pcm').exists() and not (ROOT/'capture/request-summary.json').exists(),'Omitted original unexpectedly present')
    samples=array.array('h');samples.frombytes(audio)
    if sys.byteorder!='little':samples.byteswap()
    clipping=sum(sample in (-32768,32767)for sample in samples)
    require(clipping==0 and min(samples)==-17071 and max(samples)==20751,'Sample metric mismatch')
    replay_results=runpy.run_path(str(ROOT/'verify_replays.py'))['verify'](ROOT,audio)
    print(json.dumps({'outcome':'passed','publicationFiles':len(listed),'incomingMessages':len(incoming),'outgoingMessages':len(outgoing),
        'upstreamBytes':len(audio),'upstreamSha256':digest(audio),'renderedBytes':len(rendered),'speechTimePaddingBytes':0,
        'tailPaddingBytes':sum(pad for start,pad in padding),'fullScaleClippingSamples':clipping,'devicePassedRealTail':True,'offlineReplays':replay_results}))

if __name__=='__main__':main()
