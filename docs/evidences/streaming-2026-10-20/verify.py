"""Offline archive verification. No credentials, network, or audio device access."""

import csv
import hashlib
import json
import math
import re
import struct
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent
CASE_NAMES = ("pcm-long", "mp3-long", "pcm-cancel")
STAGES = ("Request", "Connected", "PrefixSent", "FirstAudio", "FirstPcm", "PlaybackStarted", "Finish", "Drained")
MODEL = "s2.1-pro-free"
ENDPOINT = "wss://api.fish.audio/v1/tts/live"
PLUGIN_SHA256 = "c02a1c36ef7ee6ef15e9419d3e815e605cb8d072a318f00d0c79cb8530ccc8d7"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read_json(path):
    return json.loads(path.read_bytes())


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


def inspect_json(value):
    if isinstance(value, dict):
        for key, item in value.items():
            if key.lower() == "authorization":
                require(item == "[REDACTED]", "Authorization must be fully redacted")
            require(key.lower() not in ("cookie", "set-cookie", "apikey", "api_key"), "Unexpected sensitive metadata field")
            inspect_json(item)
    elif isinstance(value, list):
        for item in value:
            inspect_json(item)
    elif isinstance(value, str):
        require(not re.search(r"[A-Za-z]:[\\/](?:Users|Projects)[\\/]|/(?:Users|home)/", value, re.I), "Machine-local path in JSON")
        for url in re.findall(r"(?:https?|wss?)://[^\s<>\"'\\]+", value, re.I):
            require(url == ENDPOINT, "Untrusted URL in JSON")


def verify_case(name):
    directory = ROOT / "cases" / name
    summary = read_json(directory / "summary.json")
    request = read_json(directory / "request-summary.json")
    observations = read_json(directory / "observations.json")
    timeline = read_json(directory / "timeline.json")
    cancel = name.endswith("cancel")
    expected_outcome = "cancelled-after-playback" if cancel else "completed"
    require(summary["outcome"] == summary["nativeOutcome"] == expected_outcome, "Unexpected case outcome: " + name)
    require(summary["verification"] == {"Passed": True, "Failures": []}, "Recorder verification failed: " + name)
    require(summary["failureType"] == ("System.OperationCanceledException" if cancel else None), "Unexpected exception classification")
    require(request["headers"] == {"Authorization": "[REDACTED]", "model": MODEL}, "Unexpected request headers")
    require(request["endpoint"] == ENDPOINT and request["endpointParameters"] == {} and request["retryCount"] == 0, "Unexpected endpoint/retry policy")
    require(request["modelGuard"]["exact"] == MODEL and request["modelGuard"]["checkedBeforeConnectAndEverySend"], "Missing model guard")
    require(request["productionAssembly"]["sha256"] == PLUGIN_SHA256, "Production DLL identity mismatch")
    require(request["textUtf16Length"] == len(request["text"]) == 215, "Unexpected neutral input")
    require(observations == summary["stageObservations"], "Observation files disagree")
    require([item["stage"] for item in observations] == list(STAGES), "Stage slots missing/out of order")
    for item in observations:
        absent = cancel and item["stage"] in ("Finish", "Drained")
        require((item["elapsedMs"] is None) == absent, "Incorrect stage completeness")
        require(item["origin"] == ("not-observed" if absent else "native-completion-observer"), "Incorrect native stage origin")
    native = {item["stage"]: item["elapsedMs"] for item in observations}
    require(native["FirstAudio"] <= native["FirstPcm"] <= native["PlaybackStarted"], "Native start order failed")
    counters = summary["counters"]
    snapshot = counters["sourceAtActualPlayback"]
    require(snapshot == {"HighestIndex": 99, "ReadCount": 100, "SentTextUtf16": 53,
                         "CompletedTextEvents": 1, "StopCompleted": False}, "Prefix snapshot mismatch")
    require(counters["highestReadIndexBeforeActualPlayback"] == 99, "Source tail read early")
    require(not summary["anyArtifactRedacted"] and not counters["captureLimitReached"], "Capture redacted/truncated")
    require(summary["captureBytes"] < summary["captureLimitBytes"] == 8388608, "Capture budget mismatch")

    expected_files = {"summary.json"}
    capture_bytes = 0
    for artifact in summary["artifacts"]:
        path = directory / artifact["Name"]
        require(path.parent == directory, "Artifact path must be a plain filename")
        data = path.read_bytes()
        require(len(data) == artifact["PersistedBytes"] == artifact["OriginalBytes"], "Artifact size mismatch: " + name + "/" + path.name)
        require(digest(data) == artifact["PersistedSha256"] == artifact["OriginalSha256"], "Artifact hash mismatch: " + name + "/" + path.name)
        require(not artifact["Redacted"] and artifact["RedactionReason"] is None, "Unexpected whole-artifact redaction")
        if path.suffix in (".msgpack", ".pcm", ".mp3"):
            require(not re.search(rb"(?:https?|wss?)://", data, re.I), "Unexpected URL in raw binary artifact")
            capture_bytes += len(data)
        expected_files.add(path.name)
    require({path.name for path in directory.iterdir() if path.is_file()} == expected_files, "Unexpected/missing case file")
    require(capture_bytes == summary["captureBytes"], "Capture-byte total mismatch")
    require(not list(directory.glob("*incomplete*")), "These case recordings should have no incomplete messages")

    incoming = [decode(path) for path in sorted(directory.glob("incoming-*.msgpack"))]
    outgoing = [decode(path) for path in sorted(directory.glob("outgoing-*.msgpack"))]
    expected_events = ["start", "text", "flush"] if cancel else ["start", "text", "flush", "text", "flush", "stop"]
    require([item["event"] for item in outgoing] == expected_events, "Unexpected outgoing protocol sequence")
    sent_text = "".join(item["text"] for item in outgoing if item["event"] == "text")
    require(sent_text == (request["text"][:53] if cancel else request["text"]), "Outgoing text mismatch")
    require(outgoing[0]["request"]["format"] == summary["format"] and outgoing[0]["request"]["sample_rate"] == 44100, "Audio start parameters mismatch")
    audio = b"".join(item["audio"] for item in incoming if item["event"] == "audio")
    require(audio == (directory / ("raw-audio." + summary["format"])).read_bytes(), "Concatenated audio mismatch")
    require(len(audio) == counters["receivedAudioBytes"], "Audio-byte count mismatch")
    require(len(incoming) == counters["receivedBinaryMessages"], "Complete message count mismatch")
    fragments = [item for item in timeline if item["kind"] == "receiveFragment"]
    require(len(fragments) == counters["receivedFragments"], "Receive fragment count mismatch")
    require(sum(item["details"]["EndOfMessage"] for item in fragments) == len(incoming), "Complete receive boundary count mismatch")
    require([item["sequence"] for item in timeline] == list(range(len(timeline))), "Timeline sequence mismatch")
    kinds = [item["kind"] for item in timeline]
    require(kinds.index("waveOutInit") < kinds.index("waveOutPlayEntry") < kinds.index("waveOutPlayReturned") < kinds.index("actualPlaybackCallbackEntry"), "Actual WaveOut callback ordering mismatch")
    callback = next(item for item in timeline if item["kind"] == "actualPlaybackCallbackEntry")
    require(callback["details"] == snapshot, "Timeline callback snapshot differs")
    early_sends = [item["details"]["event"] for item in timeline
                   if item["kind"] == "sendAttempt" and item["sequence"] < callback["sequence"]]
    require(early_sends == ["start", "text", "flush"], "Tail/stop attempted before actual playback")
    if cancel:
        require(kinds.index("actualPlaybackCallbackEntry") < kinds.index("cancelAfterActualPlaybackCallback") < kinds.index("playbackCancellationRequested"), "Cancellation preceded actual playback")
        require(counters["completedStopEvents"] == 0 and counters["indexedReadsAfterActualPlayback"] == 0, "Cancelled case consumed tail")
        require(all(item["event"] == "audio" for item in incoming), "Unexpected successful finish in cancellation")
    else:
        require(incoming[-1]["event"] == "finish" and incoming[-1]["reason"] == "stop", "Missing successful stop finish")
        require(native["Finish"] < native["Drained"] and counters["highestDevicePlayedBytes"] > 0, "Missing native drain/device progress")
        require(counters["completedStopEvents"] == 1 and counters["sentTextUtf16"] == 215, "Incomplete long text")
        require(counters["playbackBeforeStopSendCompleted"], "Stop completed before playback")
    return {"name": name, "classification": "production-live-capture",
            "outcome": expected_outcome, "audioCompleteness": "intentional-prefix-only-cancellation" if cancel else "successful-stop-and-device-drain",
            "storedWebSocketMessages": "complete; no partial-message file", "rawAudioBytes": len(audio),
            "binaryAndAudioCaptureBytes": capture_bytes, "directoryBytes": sum(path.stat().st_size for path in directory.iterdir() if path.is_file()),
            "productionAssemblySha256": PLUGIN_SHA256}


def distribution(values):
    values = sorted(values)
    mean = sum(values) / len(values)
    def rank(fraction):
        return values[math.ceil(fraction * len(values)) - 1]
    return {"count": len(values), "min": values[0], "median": rank(0.5), "p90": rank(0.9), "p95": rank(0.95),
            "p99": rank(0.99), "max": values[-1], "mean": mean,
            "standardDeviation": math.sqrt(sum((value - mean) ** 2 for value in values) / len(values))}


def verify_benchmark():
    directory = ROOT / "prefix-benchmark"
    result = read_json(directory / "results.json")
    sample_bytes = (directory / "samples.csv").read_bytes()
    require(result["classification"] == "offline-first-prefix-microbenchmark" and result["outcome"] == "passed", "Benchmark classification mismatch")
    require(result["productionAssembly"]["sha256"] == PLUGIN_SHA256, "Benchmark DLL mismatch")
    require(result["samples"]["sha256"] == digest(sample_bytes) and result["samples"]["bytes"] == len(sample_bytes), "Benchmark sample integrity mismatch")
    require(result["networkCalls"] == result["audioDeviceOperations"] == result["environmentCredentialReads"] == 0, "Benchmark must stay offline")
    require(result["source"]["tailReadSentinelIndex"] == 100 and result["source"]["sentinelSelfCheckPassed"]
            and result["source"]["wholeInputStringAllocated"] is False, "Benchmark source scope mismatch")
    grouped = defaultdict(list)
    with (directory / "samples.csv").open(newline="", encoding="utf-8") as stream:
        for row in csv.DictReader(stream):
            grouped[int(row["virtual_length"])].append({key: int(value) for key, value in row.items()})
    require(sorted(grouped) == [1000, 1000000, 1000000000], "Unexpected benchmark lengths")
    for entry in result["results"]:
        rows = grouped[entry["virtualLengthUtf16"]]
        require(entry["warmupIterations"] == 1000 and entry["measuredIterations"] == len(rows) == 10000, "Benchmark iteration count mismatch")
        require([row["repetition"] for row in rows] == list(range(10000)), "Benchmark sample sequence mismatch")
        require(entry["indexedReadsPerIteration"] == 100 and entry["highestIndexRead"] == 99
                and entry["firstForbiddenIndex"] == 100 and entry["prefixUtf16Length"] == 53, "Benchmark prefix invariant mismatch")
        require(entry["prefixSha256"] == digest(result["source"]["expectedPrefix"].encode()), "Benchmark prefix hash mismatch")
        series = {
            "readNextAllocatedBytes": [row["readnext_allocated_bytes"] for row in rows],
            "setupPlusReadNextAllocatedBytes": [row["setup_plus_readnext_allocated_bytes"] for row in rows],
            "readNextElapsedMicroseconds": [row["readnext_elapsed_ticks"] * 1000000.0 / result["stopwatchFrequency"] for row in rows],
        }
        for key, values in series.items():
            for statistic, expected in distribution(values).items():
                require(math.isclose(entry[key][statistic], expected, rel_tol=1e-9, abs_tol=1e-9), "Benchmark statistic mismatch: " + key + "/" + statistic)
    return {"classification": result["classification"], "virtualLengths": sorted(grouped), "measuredSamples": sum(map(len, grouped.values())),
            "wholeInputStringAllocated": False, "productionAssemblySha256": PLUGIN_SHA256}


def verify_host_observations():
    """Verify separate sanitized host records without changing the raw case index."""
    directory = ROOT.parent / "streaming-host-2026-10-20"
    expected = {
        "host-mp3-streaming.json": ("6aed28e1407ad1cdb4aa9f528807123286b76309c9501d394395cf898f44d943", "1.1.2.6", "mp3", "completed-through-device-drain"),
        "host-pcm-first-attempt.json": ("4e03360143264d7c7063291682e736bfd18436fc05d013eacc203dc0ccfcb08c", "1.1.2.7", "pcm", "connection-timeout-with-secondary-ui-cleanup-failure"),
        "host-pcm-streaming.json": ("5b5ad21f0fd4a385dcbcefefdbc612fb339471e261d23353e80cd1fb55ece98d", "1.1.2.8", "pcm", "completed-through-device-drain"),
    }
    records = []
    for name, (sha256, version, audio_format, outcome) in expected.items():
        data = (directory / name).read_bytes()
        require(digest(data) == sha256, "Separate host record changed: " + name)
        item = json.loads(data)
        inspect_json(item)
        require(item["hostVersion"] == "2.0.10" and item["validationVersion"] == version
                and item["format"] == audio_format and item["model"] == MODEL and item["testKeyEqualityChecked"], "Host case metadata mismatch")
        stage_matches = [re.search(r'Fish Audio streaming "([^"]+)" at ([0-9.]+) ms', line) for line in item["lines"]]
        stages = {match[1]: float(match[2]) for match in stage_matches if match}
        if name == "host-pcm-first-attempt.json":
            require(stages == {"Request": 0}, "Historical failed PCM attempt relabeled")
            require(item["classification"] == "host-integration-failure"
                    and item["outcome"] == "connection-timeout-before-connected; secondary UI-thread cleanup exception", "Historical failure classification changed")
        else:
            require(len(stages) == len(STAGES) and set(stages) == set(STAGES), "Successful host case must retain all eight native stages")
            require(stages["FirstAudio"] <= stages["FirstPcm"] <= stages["PlaybackStarted"] < stages["Drained"]
                    and stages["Finish"] < stages["Drained"], "Host native stage order mismatch")
        if name == "host-pcm-streaming.json":
            require(item["classification"] == "host-integration-success" and item["outcome"] == outcome, "Final PCM host classification mismatch")
            require(item["productionAssemblySha256"] == "bf191c2af6e87e93175cd3f3991053f22ed8846910d5fa3a98d2dbd2656e743c", "Corrected host DLL identity mismatch")
            require(item["settingsViewOpenedThenClosedBeforeCall"] and item["threadAccessExceptionObserved"] is False, "Settings-view cleanup acceptance missing")
            require(item["settingsObservations"] == {
                "keyClearedAndReenteredWhilePasswordBoxRetainedFocus": True,
                "keySavedBeforeFocusChange": True,
                "legacySchema2FormatBeforeManualSwitch": "mp3",
                "retainedMp3Bitrate": 192,
                "pcmFormatSurvivedHostRestart": True,
                "bitrateControlDisabledForPcm": True,
            }, "Host settings observations differ")
        records.append({"path": "../streaming-host-2026-10-20/" + name, "sha256": sha256,
                        "validationVersion": version, "format": audio_format, "outcome": outcome})
    return {"record": "../streaming-host-2026-10-20.md",
            "accepted": "installed-host short-text MP3/PCM playback through device drain and the separately recorded settings observations",
            "scopeLimit": "long-text tail gating and cancellation remain isolated production-harness evidence; no corresponding installed-host cases are claimed",
            "historicalFailure": "original PCM connection timeout and secondary UI cleanup failure remain immutable; the timeout cause is unknown",
            "records": records}


def classification(path):
    if path.parts[0] == "cases":
        if path.suffix == ".msgpack":
            return "production-live-complete-websocket-message"
        if path.suffix in (".pcm", ".mp3"):
            return "production-live-intentionally-cancelled-audio-prefix" if path.parts[1] == "pcm-cancel" else "production-live-audio-successful-session"
        return "production-live-sanitized-metadata"
    if path.parts[0] == "harness":
        return "production-validation-harness-source" if path.suffix in (".cs", ".csproj") else "reproduction-instructions"
    if path.parts[0] == "prefix-benchmark":
        return "offline-prefix-benchmark-source" if path.suffix in (".cs", ".csproj") else "offline-prefix-benchmark-measurements" if path.suffix in (".csv", ".json") else "reproduction-instructions"
    return "offline-integrity-verifier" if path.name == "verify.py" else "evidence-interpretation"


def inventory():
    result = []
    for path in sorted(ROOT.rglob("*")):
        relative = path.relative_to(ROOT)
        if not path.is_file() or relative == Path("manifest.json") or any(part in ("bin", "obj", "__pycache__") for part in relative.parts):
            continue
        data = path.read_bytes()
        if path.suffix == ".json":
            inspect_json(json.loads(data))
        result.append({"path": relative.as_posix(), "classification": classification(relative), "bytes": len(data), "sha256": digest(data)})
    return result


def main():
    require(sys.argv[1:] in ([], ["--write-manifest"]), "Usage: verify.py [--write-manifest]")
    cases = [verify_case(name) for name in CASE_NAMES]
    benchmark = verify_benchmark()
    host = verify_host_observations()
    files = inventory()
    contents = {"schema": 1, "classification": "production-streaming-evidence-with-separate-offline-benchmark",
                "captureDateUtc": "2026-10-20", "keyPublication": "No key retained; Authorization is fully redacted in recorded safe request summaries.",
                "physicalListeningClaimed": False, "hostUiAcceptance": host,
                "clockDomains": {"observations": "native session activity stopwatch", "timeline": "independent harness stopwatch"},
                "cases": cases, "offlineBenchmark": benchmark, "fileCount": len(files), "totalBytesExcludingThisManifest": sum(item["bytes"] for item in files),
                "inventoryExcludes": ["manifest.json (this index)", "generated bin/obj/__pycache__ directories"], "files": files}
    manifest_path = ROOT / "manifest.json"
    if sys.argv[1:]:
        manifest_path.write_bytes((json.dumps(contents, indent=2, ensure_ascii=False) + "\n").encode("utf-8"))
    require(read_json(manifest_path) == contents, "Archive index differs; inspect changes before regenerating with --write-manifest")
    print(json.dumps({"outcome": "passed", "liveCases": len(cases), "offlineBenchmarkSamples": benchmark["measuredSamples"],
                      "files": len(files), "bytesExcludingManifest": contents["totalBytesExcludingThisManifest"]}))


if __name__ == "__main__":
    main()
