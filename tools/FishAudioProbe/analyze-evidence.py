import array
import hashlib
import json
import math
import pathlib
import subprocess
import sys

root = pathlib.Path(sys.argv[1])
rows = []
timings = []
for case_path in root.glob('run-*/cases/*/case.json'):
    case = json.loads(case_path.read_text(encoding='utf-8'))
    body = case.get('body')
    if body:
        data = (case_path.parent / body['file']).read_bytes()
        assert len(data) == body['storedBytes'], case_path
        assert hashlib.sha256(data).hexdigest() == body['storedSha256'], case_path
    else:
        continue
    if case['endpoint'] not in ('http-tts', 'ws-tts') or case['outcome'] != 'success':
        continue
    request = json.loads((case_path.parent / 'request.json').read_text(encoding='utf-8'))
    fmt = case.get('details', {}).get('format') or request['body']['format']
    row = {'case': case['id'], 'path': str(case_path.parent.relative_to(root)).replace('\\', '/'), 'format': fmt, 'bytes': len(data)}
    if fmt == 'pcm':
        samples = array.array('h', data)
        if sys.byteorder != 'little':
            samples.byteswap()
        def stats(values):
            rms = math.sqrt(sum(v*v for v in values)/len(values))
            delta = math.sqrt(sum((b-a)**2 for a,b in zip(values, values[1:]))/(len(values)-1))
            return {'rms': round(rms, 3), 'rmsSuccessiveDifference': round(delta, 3), 'peak': max(abs(v) for v in values), 'mean': round(sum(values)/len(values),3)}
        row.update({'sampleCount':len(samples), 'durationSecondsAt44100':len(samples)/44100, 'signed16LittleEndian':stats(samples)})
        samples.byteswap()
        row['signed16BigEndian'] = stats(samples)
    else:
        result = subprocess.run(['ffprobe', '-v', 'error', '-select_streams', 'a:0', '-show_entries', 'stream=codec_name,sample_rate,channels,bit_rate,duration', '-of', 'json', str(case_path.parent/body['file'])], capture_output=True, check=True)
        row['ffprobe'] = json.loads(result.stdout)['streams']
        subprocess.run(['ffmpeg', '-v', 'error', '-i', str(case_path.parent/body['file']), '-f', 'null', '-'], capture_output=True, check=True)
        row['fullDecodeToNullExitCode'] = 0
    rows.append(row)
    if case['endpoint'] == 'ws-tts':
        events = json.loads((case_path.parent/'events.json').read_text(encoding='utf-8'))
        audio = next(e for e in events if e['event'] == 'audio' and e['direction']=='receive')
        tail = next(e for e in events if e['event']=='text' and e['direction']=='send' and e['order'] > audio['order'])
        stop = next(e for e in events if e['event']=='stop' and e['direction']=='send')
        finish = next(e for e in events if e['event']=='finish' and e['direction']=='receive')
        assert audio['order'] < tail['order'] < stop['order'] < finish['order']
        timings.append({'case':case['id'], 'firstAudioMs':audio['elapsedMs'], 'firstTailSendMs':tail['elapsedMs'], 'stopSendMs':stop['elapsedMs'], 'finishMs':finish['elapsedMs'], 'orderedAudioBeforeTailAndStop':True})
report={'method':'Offline byte interpretation, adjacent-sample statistics, ffprobe and full MP3 decode. No sound-device or subjective listening claim.', 'pcmConclusion':'Signed 16-bit little-endian at requested 44100 Hz mono is an engineering interpretation supported by markedly smoother adjacent samples; not an official signedness/endianness guarantee. Physical output validation belongs to runtime integration.', 'audio':rows, 'nativeWebSocketTimings':timings}
(root/'audio-analysis.json').write_text(json.dumps(report,indent=2)+'\n', encoding='utf-8')
print(json.dumps({'audioCasesAnalyzed':len(rows), 'nativeWebSocketProofs':len(timings), 'allBodyHashesValid':True}))
