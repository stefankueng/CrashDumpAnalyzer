(() => {
    const form = document.getElementById('upload-form');
    const field = document.getElementById('files-field');
    const label = document.getElementById('files-label');
    const progress = document.getElementById('files-progress');
    const defaultText = label.textContent;

    function setDropZone(text, active) {
        label.textContent = text;
        label.classList.toggle('upload-active', active);
    }

    function submitFiles() {
        if (field.files.length === 0) return;
        setDropZone('Uploading dump files...', true);
        const data = new FormData();
        for (const file of field.files) {
            data.append(field.name, file, file.name);
        }
        const request = new XMLHttpRequest();
        request.upload.addEventListener('progress', event => {
            if (event.lengthComputable) {
                const percentage = Math.round(event.loaded / event.total * 100) + '%';
                progress.textContent = percentage;
                progress.style.width = percentage;
            }
        });
        request.addEventListener('loadend', () => {
            progress.textContent = '';
            progress.style.width = '';
            field.value = '';
            setDropZone(defaultText, false);
            if (request.status === 201) {
                location.reload();
            } else {
                alert(request.status + ': ' + request.statusText + '\n' + request.responseText);
            }
        });
        request.open(form.method, form.action, true);
        request.send(data);
    }

    form.addEventListener('submit', event => {
        event.preventDefault();
        submitFiles();
    });
    field.addEventListener('change', submitFiles);
    for (const type of ['dragover', 'dragenter', 'dragleave', 'drop']) {
        label.addEventListener(type, event => {
            event.preventDefault();
            event.stopPropagation();
            const active = type === 'dragover' || type === 'dragenter';
            setDropZone(active ? 'Drop the dump files to upload them.' : defaultText, active);
            if (type === 'drop') {
                field.files = event.dataTransfer.files;
                submitFiles();
            }
        });
    }
})();
