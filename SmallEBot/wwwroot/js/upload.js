// SmallEBot: chunked file upload via API controller and drop zone
(function () {
    window.SmallEBot = window.SmallEBot || {};

    SmallEBot.uploadFileViaApi = async function (dotNetRef, file, chunkSize) {
        chunkSize = chunkSize || 65536;
        let startRes = await fetch('/api/workspace/upload/start', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ fileName: file.name, contentLength: file.size })
        });
        if (!startRes.ok) {
            let err = await startRes.text();
            await dotNetRef.invokeMethodAsync('OnUploadFailed', err || 'Start upload failed');
            return;
        }
        let startJson = await startRes.json();
        let uploadId = startJson.uploadId;
        if (!uploadId) {
            await dotNetRef.invokeMethodAsync('OnUploadFailed', 'No uploadId returned');
            return;
        }
        await dotNetRef.invokeMethodAsync('AddPendingUpload', uploadId, file.name);

        let sent = 0;
        let total = file.size;
        while (sent < total) {
            let chunk = file.slice(sent, sent + chunkSize);
            let chunkRes = await fetch('/api/workspace/upload/chunk/' + encodeURIComponent(uploadId), {
                method: 'POST',
                headers: { 'Content-Type': 'application/octet-stream' },
                body: chunk
            });
            if (!chunkRes.ok) {
                await dotNetRef.invokeMethodAsync('OnUploadFailed', 'Chunk upload failed');
                return;
            }
            let pct = total > 0 ? (sent + chunk.size) / total * 100 : 0;
            await dotNetRef.invokeMethodAsync('ReportUploadProgress', uploadId, Math.round(pct));
            sent += chunk.size;
        }

        let completeRes = await fetch('/api/workspace/upload/complete/' + encodeURIComponent(uploadId), {
            method: 'POST'
        });
        if (!completeRes.ok) {
            await dotNetRef.invokeMethodAsync('OnUploadFailed', 'Complete upload failed');
            return;
        }
        let result = await completeRes.json();
        let path = result.path || result.Path;
        let replacedOldPath = result.replacedOldPath || result.ReplacedOldPath;
        await dotNetRef.invokeMethodAsync('OnUploadComplete', uploadId, path, replacedOldPath);
    };

    let _dropZoneListeners = {};
    SmallEBot.attachDropZone = function (elementId, dotNetRef) {
        let el = document.getElementById(elementId);
        if (!el) return;
        SmallEBot.detachDropZone(elementId);
        let dragover = function (e) { e.preventDefault(); };
        let drop = function (e) {
            e.preventDefault();
            let files = e.dataTransfer && e.dataTransfer.files;
            if (!files) return;
            for (let i = 0; i < files.length; i++) {
                (function (f) {
                    SmallEBot.uploadFileViaApi(dotNetRef, f, 65536);
                })(files[i]);
            }
        };
        el.addEventListener('dragover', dragover);
        el.addEventListener('drop', drop);
        _dropZoneListeners[elementId] = { dragover: dragover, drop: drop };
    };
    SmallEBot.detachDropZone = function (elementId) {
        let el = document.getElementById(elementId);
        let stored = _dropZoneListeners[elementId];
        if (el && stored) {
            el.removeEventListener('dragover', stored.dragover);
            el.removeEventListener('drop', stored.drop);
        }
        delete _dropZoneListeners[elementId];
    };
})();
