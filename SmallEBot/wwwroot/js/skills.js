// SmallEBot: folder picker for skill import (File System Access API or fallback input)
window.SmallEBotSkills = window.SmallEBotSkills || {};

window.SmallEBotSkills.pickFolder = async function () {
    if (typeof showDirectoryPicker !== 'undefined') {
        try {
            var dirHandle = await showDirectoryPicker({ mode: 'read' });
            var result = await readDirectoryRecursive(dirHandle, '');
            return { folderName: dirHandle.name, files: result };
        } catch (e) {
            if (e.name === 'AbortError') return null;
            throw e;
        }
    }
    return new Promise(function (resolve) {
        var input = document.createElement('input');
        input.type = 'file';
        input.webkitdirectory = true;
        input.directory = true;
        input.multiple = true;
        input.style.display = 'none';
        input.onchange = async function () {
            document.body.removeChild(input);
            var files = input.files;
            if (!files || files.length === 0) { resolve(null); return; }
            var folderName = '';
            var fileContents = {};
            for (var i = 0; i < files.length; i++) {
                var f = files[i];
                var path = (f.webkitRelativePath || f.name).replace(/\\/g, '/');
                if (!folderName && path.indexOf('/') !== -1)
                    folderName = path.split('/')[0];
                else if (!folderName)
                    folderName = path;
                try {
                    var text = await f.text();
                    var relativePath = path.indexOf('/') !== -1 ? path.substring(path.indexOf('/') + 1) : path;
                    fileContents[relativePath] = text;
                } catch (err) {
                    var relativePath = path.indexOf('/') !== -1 ? path.substring(path.indexOf('/') + 1) : path;
                    fileContents[relativePath] = '';
                }
            }
            if (!folderName && files.length > 0) folderName = 'skill';
            resolve({ folderName: folderName, files: fileContents });
        };
        input.oncancel = function () {
            document.body.removeChild(input);
            resolve(null);
        };
        document.body.appendChild(input);
        input.click();
    });
};

async function readDirectoryRecursive(dirHandle, basePath) {
    var result = {};
    var iter = dirHandle.entries();
    while (true) {
        var entry = await iter.next();
        if (entry.done) break;
        var name = entry.value[0];
        var handle = entry.value[1];
        var path = basePath ? basePath + '/' + name : name;
        if (handle.kind === 'file') {
            try {
                var file = await handle.getFile();
                var text = await file.text();
                result[path] = text;
            } catch (err) {
                result[path] = '';
            }
        } else {
            var sub = await readDirectoryRecursive(handle, path);
            for (var k in sub) result[k] = sub[k];
        }
    }
    return result;
}

// Expose for Blazor JSInvoke (returns serializable { folderName, files })
window.SmallEBotPickSkillFolder = function () { return window.SmallEBotSkills.pickFolder(); };
