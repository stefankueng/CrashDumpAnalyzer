(() => {
    const modal = document.getElementById('deleteOldEntriesModal');
    const versionInput = document.getElementById('deleteVersionInput');
    const inputSection = document.getElementById('deleteInputSection');
    const progressSection = document.getElementById('deleteProgressSection');
    const resultMessage = document.getElementById('deleteResultMessage');
    const progressBar = document.getElementById('deleteProgressBar');
    const progressText = document.getElementById('deleteProgressText');
    const progressCounter = document.getElementById('deleteProgressCounter');
    const progressDetails = document.getElementById('deleteProgressDetails');
    const startBtn = document.getElementById('deleteStartBtn');
    const cancelBtn = document.getElementById('deleteCancelBtn');
    const closeBtn = document.getElementById('deleteModalCloseBtn');
    const reloadBtn = document.getElementById('deleteReloadBtn');

    startBtn.addEventListener('click', async () => {
        const version = versionInput.value.trim();
        if (!version) {
            alert('Please enter a version number');
            return;
        }
        if (!/^(\d+\.\d+\.\d+\.\d+)$/.test(version)) {
            alert('Please enter a valid version number in the format 1.2.3.4');
            return;
        }
        if (!confirm(`Are you sure you want to delete all entries with version lower than ${version} that don't have tickets assigned?`)) return;

        inputSection.hidden = true;
        progressSection.hidden = false;
        resultMessage.hidden = true;
        startBtn.hidden = true;
        cancelBtn.disabled = true;
        closeBtn.disabled = true;
        progressText.textContent = 'Counting entries to delete...';
        progressCounter.textContent = '0 / ?';
        progressBar.style.width = '0%';
        progressBar.textContent = '0%';
        progressBar.setAttribute('aria-valuenow', 0);

        try {
            const { count: totalCount } = await $.ajax({
                url: '/Api/CountOldEntries?version=' + encodeURIComponent(version),
                type: 'GET'
            });
            if (totalCount === 0) {
                progressSection.hidden = true;
                resultMessage.className = 'alert alert-info';
                resultMessage.textContent = 'No entries found to delete.';
                resultMessage.hidden = false;
                cancelBtn.disabled = false;
                closeBtn.disabled = false;
                return;
            }
            progressText.textContent = `Deleting ${totalCount} entries...`;
            progressCounter.textContent = `0 / ${totalCount}`;
            let deletedCount = 0;
            const startTime = Date.now();
            while (deletedCount < totalCount) {
                const batch = await postCallstackAction('DeleteOldEntries', { version, batchSize: 10 });
                deletedCount += batch.deletedCount;
                const progress = Math.min(100, Math.round(deletedCount / totalCount * 100));
                progressBar.style.width = progress + '%';
                progressBar.textContent = progress + '%';
                progressBar.setAttribute('aria-valuenow', progress);
                progressCounter.textContent = `${deletedCount} / ${totalCount}`;
                const elapsed = Math.max(0.001, (Date.now() - startTime) / 1000);
                const rate = deletedCount / elapsed;
                const remaining = totalCount - deletedCount;
                const eta = remaining > 0 && rate > 0 ? Math.round(remaining / rate) : 0;
                progressDetails.textContent = `Deleted ${deletedCount} entries (${rate.toFixed(1)} per second). ETA: ${eta} seconds`;
                if (batch.deletedCount === 0) break;
                await new Promise(resolve => setTimeout(resolve, 100));
            }
            progressBar.classList.remove('progress-bar-animated');
            progressSection.hidden = true;
            resultMessage.className = 'alert alert-success';
            resultMessage.textContent = `Successfully deleted ${deletedCount} entries.`;
            resultMessage.hidden = false;
            reloadBtn.hidden = false;
            closeBtn.disabled = false;
        } catch (error) {
            progressSection.hidden = true;
            resultMessage.className = 'alert alert-danger';
            resultMessage.textContent = 'Error deleting entries: ' + (error.responseText || error.statusText || error);
            resultMessage.hidden = false;
            cancelBtn.disabled = false;
            closeBtn.disabled = false;
        }
    });

    reloadBtn.addEventListener('click', () => location.reload());
    modal.addEventListener('hidden.bs.modal', () => {
        inputSection.hidden = false;
        progressSection.hidden = true;
        resultMessage.hidden = true;
        versionInput.value = '';
        startBtn.hidden = false;
        reloadBtn.hidden = true;
        cancelBtn.disabled = false;
        closeBtn.disabled = false;
        progressBar.classList.add('progress-bar-animated');
        progressDetails.textContent = '';
    });
})();
