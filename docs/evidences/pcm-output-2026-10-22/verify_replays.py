"""Standard-library verification of saved offline playback comparison artifacts."""
import hashlib,json
def verify(root,audio):
    digest=lambda data:hashlib.sha256(data).hexdigest()
    load=lambda name:json.loads((root/name).read_bytes())
    require=lambda condition,message:None if condition else fail(message)
    clip=audio[:1323000];results=[]
    for mode,buffers in [('100x2',2),('200x4',4)]:
        prefix='offline-replay/'+mode+'/'
        summary=load(prefix+'summary.json');reads=load(prefix+'provider-reads.json')
        positions=load(prefix+'device-positions.json');events=load(prefix+'events.json')
        descriptor=load(prefix+'rendered-reconstruction.json')
        require(summary['outcome']=='completed' and summary['configuration']==mode,'Replay outcome/configuration')
        require(summary['noNetwork'] and summary['noCredentials'],'Replay network/credential policy')
        require(summary['replaySha256']==digest(clip),'Replay source identity')
        cursor=0;source=0;tail=0;rendered=[];padding=[]
        for sequence,read in enumerate(reads):
            require(sequence==read['Sequence'] and read['RenderedByteOffset']==cursor==read['DeviceSourceCursorBefore'],'Replay read cursor')
            real=read['RealBytes'];pad=read['PaddingBytes'];count=read['ReturnedBytes']
            require(read['RequestedBytes']==count==4410 and real+pad==count,'Replay read size')
            part=clip[source:source+real];require(len(part)==real,'Replay real range');rendered.append(part+bytes(pad))
            source+=real
            if real:tail=cursor+real
            require(tail==read['LastRealPcmEnd'],'Replay real tail')
            if pad:padding.append((cursor+real,pad))
            cursor+=count
        reconstructed=b''.join(rendered)
        require(source==len(clip) and reconstructed[:len(clip)]==clip,'Replay real bytes')
        require(all(start>=len(clip)for start,pad in padding),'Replay speech-time padding')
        require(digest(reconstructed)==descriptor['sha256'] and len(reconstructed)==descriptor['bytes'],'Omitted replay render fingerprint')
        require(descriptor['reconstruct']['prefixSha256']==digest(clip),'Replay reconstruction source')
        require(descriptor['reconstruct']['zeroSuffixBytes']==sum(pad for start,pad in padding),'Replay reconstruction suffix')
        require(max(item['Bytes']for item in positions)>=tail==len(clip),'Replay device drain')
        gaps=[after['ElapsedMs']-before['ElapsedMs']for before,after in zip(reads,reads[1:])]
        start=next(event['elapsedMs']for event in events if event['kind']=='playback-started')
        stop=next(event['elapsedMs']for event in events if event['kind']=='stop')
        plateaus=[];begin=0
        for i in range(1,len(positions)+1):
            if i<len(positions) and positions[i]['Bytes']==positions[begin]['Bytes']:continue
            duration=positions[i-1]['ElapsedMs']-positions[begin]['ElapsedMs']
            if duration>10 and positions[begin]['ElapsedMs']-start>100:plateaus.append(duration)
            begin=i
        result={'case':mode,'callbackToStopMs':stop-start,'gapCountAbove75Ms':sum(gap>75 for gap in gaps),
            'steadyStateGapCountAbove75Ms':sum(gap>75 for gap in gaps[buffers:]),
            'steadyStateMaximumGapMs':max(gaps[buffers:]),'positionPlateausAbove10MsAfterStartup':len(plateaus),
            'tailPaddingBytes':sum(pad for start,pad in padding)}
        expected=next(item for item in load('offline-replay/metrics.json')if item['case']==mode)
        require(abs(result['callbackToStopMs']-expected['callbackToStopMs'])<1e-6,'Replay timing metric')
        require(result['gapCountAbove75Ms']==expected['gapsAbove75Ms'],'Replay gap metric')
        require(result['steadyStateGapCountAbove75Ms']==expected['steadyStateGapsAbove75Ms'],'Replay steady-state metric')
        require(result['positionPlateausAbove10MsAfterStartup']==len(expected['positionPlateausAbove10MsAfterStartup']),'Replay plateau metric')
        results.append(result)
    require(results[0]['steadyStateGapCountAbove75Ms']==34 and results[1]['steadyStateGapCountAbove75Ms']==0,'Expected A/B gap comparison')
    require(results[0]['positionPlateausAbove10MsAfterStartup']==34 and results[1]['positionPlateausAbove10MsAfterStartup']==0,'Expected A/B position comparison')
    return results
def fail(message):raise ValueError(message)
