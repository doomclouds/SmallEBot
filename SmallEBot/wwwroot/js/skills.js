// SmallEBot: folder picker for skill import (File System Access API or fallback input)
window.SmallEBotSkills = window.SmallEBotSkills || {};

window.SmallEBotSkills.pickFolder = async function () {
    if (typeof showDirectoryPicker !== 'undefined') {
        try {
            let dirHandle = await showDirectoryPicker({ mode: 'read' });
            let result = await readDirectoryRecursive(dirHandle, '');
            return { folderName: dirHandle.name, files: result };
        } catch (e) {
            if (e.name === 'AbortError') return null;
            throw e;
        }
    }
    return new Promise(function (resolve) {
        let input = document.createElement('input');
        input.type = 'file';
        input.webkitdirectory = true;
        input.directory = true;
        input.multiple = true;
        input.style.display = 'none';
        input.onchange = async function () {
            document.body.removeChild(input);
            let files = input.files;
            if (!files || files.length === 0) { resolve(null); return; }
            let folderName = '';
            let fileContents = {};
            for (let i = 0; i < files.length; i++) {
                let f = files[i];
                let path = (f.webkitRelativePath || f.name).replace(/\\/g, '/');
                if (!folderName && path.indexOf('/') !== -1)
                    folderName = path.split('/')[0];
                else if (!folderName)
                    folderName = path;
                try {
                    let text = await f.text();
                    let relativePath = path.indexOf('/') !== -1 ? path.substring(path.indexOf('/') + 1) : path;
                    fileContents[relativePath] = text;
                } catch (err) {
                    let relativePath = path.indexOf('/') !== -1 ? path.substring(path.indexOf('/') + 1) : path;
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
    let result = {};
    let iter = dirHandle.entries();
    while (true) {
        let entry = await iter.next();
        if (entry.done) break;
        let name = entry.value[0];
        let handle = entry.value[1];
        let path = basePath ? basePath + '/' + name : name;
        if (handle.kind === 'file') {
            try {
                let file = await handle.getFile();
                let text = await file.text();
                result[path] = text;
            } catch (err) {
                result[path] = '';
            }
        } else {
            let sub = await readDirectoryRecursive(handle, path);
            for (let k in sub) result[k] = sub[k];
        }
    }
    return result;
}

// Expose for Blazor JSInvoke (returns serializable { folderName, files })
window.SmallEBotPickSkillFolder = function () { return window.SmallEBotSkills.pickFolder(); };
