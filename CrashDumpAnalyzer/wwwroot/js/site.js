// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

function changeTheme() {
    const theme = document.documentElement.getAttribute('data-bs-theme') === 'light' ? 'dark' : 'light';
    document.documentElement.setAttribute('data-bs-theme', theme);
    localStorage.setItem('theme', theme);
    initTooltips();
}

$(document).ready(function () {
    $('.btn').each(function () {
        if (Array.from($(this).text().trim()).length === 1) {
            $(this).addClass('single-char');
        }
    });
    const checkbox = document.getElementById('flexSwitchCheckDefault');
    const theme = localStorage.getItem('theme');
    if (theme !== null) {
        checkbox.checked = !(theme === "light");
    }
});

const popoverTriggerList = document.querySelectorAll('[data-bs-toggle="popover"]')
const popoverList = [...popoverTriggerList].map(popoverTriggerEl => new bootstrap.Popover(popoverTriggerEl, { html: true, sanitize: false, container: 'body' }))

// AllowList used for tooltip sanitizer (keep sanitize enabled)
const tooltipAllowList = {
    // elementName: [allowed attributes]
    div: ['class', 'style', 'data-*'],
    pre: ['class', 'style'],
    strong: [],
    em: [],
    br: [],
    a: ['href', 'target', 'class', 'rel', 'data-*']
};

let _activeTooltips = [];

function initTooltips() {
    if (_activeTooltips && _activeTooltips.length > 0) {
        _activeTooltips.forEach(t => {
            try { t.dispose(); } catch (ex) { /* ignore */ }
        });
    }
    _activeTooltips = [];

    const tooltipTriggerList = document.querySelectorAll('[data-bs-toggle="tooltip"]')
    const tooltipList = [...tooltipTriggerList].map(tooltipTriggerEl => {
        const t = new bootstrap.Tooltip(tooltipTriggerEl, {
            html: true,
            sanitize: true,
            allowList: tooltipAllowList,
            container: 'body'
        });
        return t;
    });

    _activeTooltips = tooltipList;
}
initTooltips();

$('[data-bs-toggle="popover"]').click(function (e) {
    e.preventDefault();
    $('[data-bs-toggle="popover"]').not(this).popover('hide');
    $(this).popover('toggle');
});
$(document).click(function (e) {
    $('[data-bs-toggle="popover"]').popover('hide');

});
function toggleAllRows(checkbox) {
    var checkboxes = document.querySelectorAll('.row-checkbox');
    checkboxes.forEach(cb => cb.checked = checkbox.checked);
}
function deleteAllSelectedEntries(reallyDelete) {
    let checkedIds = [];
    document.querySelectorAll('.row-checkbox:checked').forEach(cb => {
        checkedIds.push(cb.getAttribute('data-id'));
    });

    const itemCount = checkedIds.length;
    if (itemCount === 0) {
        return;
    }

    // Ask the user to enter the number of selected items
    const userInput = prompt(`You have checked ${itemCount} items to delete.\nPlease enter that number below to confirm the deletion:`);

    if (parseInt(userInput) !== itemCount) {
        alert("The number you entered does not match the number of checked items. Deletion canceled.");
        return;
    }

    let ajaxCalls = [];
    checkedIds.forEach(function (checkedId) {
        ajaxCalls.push(
            $.ajax({
                url: '/Api/DeleteDumpCallstack?id=' + checkedId + '&reallyDelete=' + (reallyDelete || false),
                processData: false,
                contentType: false,
                type: 'POST',
            })
        );
    });

    // Wait for all AJAX calls to complete before reloading
    $.when(...ajaxCalls).done(function () {
        location.reload();
    });
}
function combineAllSelectedEntries() {
    let checkedIds = [];
    document.querySelectorAll('.row-checkbox:checked').forEach(cb => {
        checkedIds.push(cb.getAttribute('data-id'));
    });

    const itemCount = checkedIds.length;
    if (itemCount < 2) {
        return;
    }

    // Ask the user to enter the number of selected items
    const userInput = prompt(`You have checked ${itemCount} items to link together.\nPlease enter that number below to confirm:`);

    if (parseInt(userInput) !== itemCount) {
        alert("The number you entered does not match the number of checked items. Linking items canceled.");
        return;
    }

    let toId = -1;
    let ajaxCalls = [];
    checkedIds.forEach(function (checkedId) {
        if (toId === -1) {
            toId = checkedId;
        } else if (checkedId !== toId) {
            ajaxCalls.push(
                $.ajax({
                    url: '/Api/LinkCallstack?id=' + checkedId + '&toId=' + toId,
                    processData: false,
                    contentType: false,
                    type: 'POST',
                })
            );
        }
    });

    // Wait for all AJAX calls to complete before reloading
    $.when(...ajaxCalls).done(function () {
        location.reload();
    });
}

$(function () {
    $('#setFixedVersionModal').on('show.bs.modal', function (e) {
        $('.modalTextInput').val('');
        $('#fixedVersionError').html('');
        let btn = $(e.relatedTarget); // e.related here is the element that opened the modal (the button)
        let id = btn.data('id');
        $('.saveEdit').data('id', id); // then pass it to the button inside the modal
        let version = btn.closest('td').prev().find('.version').text().trim();
        let buildType = btn.closest('td').prev().find('.buildType').text().trim();
        if (version.length === 0) {
            version = btn.closest('td').find('.version').text().trim();
            buildType = btn.closest('td').find('.buildType').text().trim();
        }
        $('.modalTextInput').val(version);
        $('#filterBuildTypes input:radio').each(function () {
            if ($(this).val() === buildType) {
                $(this).prop('checked', true);
            }
        });
    })

    $('#setApplicationVersionModal').on('show.bs.modal', function (e) {
        $('.modalAppVersionInput').val('');
        $('#appVersionError').html('');
        let btn = $(e.relatedTarget); // e.related here is the element that opened the modal (the button)
        let id = btn.data('id');
        $('.saveAppEdit').data('id', id); // then pass it to the button inside the modal
        let version = btn.closest('td').prev().find('.version').text().trim();
        let buildType = btn.closest('td').prev().find('.buildType').text().trim();
        if (version.length === 0) {
            version = btn.closest('td').find('.version').text().trim();
            buildType = btn.closest('td').find('.buildType').text().trim();
        }
        $('.modalAppVersionInput').val(version);
        $('#filterAppBuildTypes input:radio').each(function () {
            if ($(this).val() === buildType) {
                $(this).prop('checked', true);
            }
        });
    })

    $('.saveAppEdit').on('click', function () {
        let id = $(this).data('id'); // the rest is just the same
        let text = $('.modalAppVersionInput').val();
        if (!text || (/^(\d+\.\d+\.\d+\.\d+)$/.test(text)))
            $('#appVersionError').html('');
        else {
            $('#appVersionError').html('Enter version number in the format 1.2.3.4');
            return;
        }
        saveApplicationVersion(id);
        $('#setApplicationVersionModal').modal('toggle'); // this is to close the modal after clicking the modal button
    })

    $('.saveEdit').on('click', function () {
        let id = $(this).data('id'); // the rest is just the same
        let text = $('.modalTextInput').val();
        if (!text || (/^(\d+\.\d+\.\d+\.\d+)$/.test(text)))
            $('#fixedVersionError').html('');
        else {
            $('#fixedVersionError').html('Enter version number in the format 1.2.3.4');
            return;
        }
        saveFixedVersion(id);
        $('#setFixedVersionModal').modal('toggle'); // this is to close the modal after clicking the modal button
    })

    $('#setTicketModal').on('show.bs.modal', function (e) {
        let btn = $(e.relatedTarget); // e.related here is the element that opened the modal (the button)
        let id = btn.data('id');
        $('.saveTicket').data('id', id); // then pass it to the button inside the modal
        let ticket = btn.closest('td').find('.ticket').text().trim();
        $('.modalTicketInput').val(ticket);
    })

    $('.saveTicket').on('click', function () {
        let id = $(this).data('id'); // the rest is just the same
        let text = $('.modalTicketInput').val();
        saveTicket(id);
        $('#setTicketModal').modal('toggle'); // this is to close the modal after clicking the modal button
    })

    $('#setCommentModal').on('show.bs.modal', function (e) {
        let btn = $(e.relatedTarget); // e.related here is the element that opened the modal (the button)
        let id = btn.data('id');
        $('.saveComment').data('id', id); // then pass it to the button inside the modal
        // Fetch the comment from the model item and set it in the modal input
        let comment = btn.closest('td').find('.comment').text().trim();
        $('.modalCommentInput').val(comment);
    })

    $('.saveComment').on('click', function () {
        console.debug("Saving comment");
        let id = $(this).data('id'); // the rest is just the same
        saveComment(id);
        $('#setCommentModal').modal('toggle'); // this is to close the modal after clicking the modal button
    })

    $('.draggable').on('dragstart', function (e) {
        {
            let id = $(this).data('id'); // the rest is just the same

            e.dataTransfer = e.originalEvent.dataTransfer;
            e.dataTransfer.setData(this.textContent, id);
            this.style.opacity = '0.4';
        }
    })
    $('.draggable').on('dragend', function () {
        {
            this.style.opacity = '1';
        }
    })
    $('.draggable').on('dragenter', function (e) {
        {
            this.classList.add('over');
        }
    })
    $('.draggable').on('dragleave', function () {
        {
            this.classList.remove('over');
        }
    })
    $('.draggable').on('dragover', function (e) {
        {
            e.dataTransfer = e.originalEvent.dataTransfer;
            e.dataTransfer.dropEffect = 'none';
            let draggedText = e.dataTransfer.types[0].trim().toLowerCase();
            let targetText = this.textContent.trim().toLowerCase();
            if (draggedText === targetText || draggedText.replace(/\.[^/.]+$/, "") === targetText.replace(/\.[^/.]+$/, "") || draggedText === "◎" || targetText === "◎") {
                e.dataTransfer.dropEffect = 'move';
            }
            e.preventDefault();
            return false;
        }
    })
    $('.draggable').on('drop', function (e) {
        {
            e.stopPropagation();
            this.classList.remove('over');
            e.dataTransfer = e.originalEvent.dataTransfer;
            e.dataTransfer.dropEffect = 'none';
            let draggedText = e.dataTransfer.types[0].trim().toLowerCase();
            let targetText = this.textContent.trim().toLowerCase();
            if (draggedText === targetText || draggedText.replace(/\.[^/.]+$/, "") === targetText.replace(/\.[^/.]+$/, "") || draggedText === "◎" || targetText === "◎") {
                let toId = $(this).data('id'); // the rest is just the same
                
                // Get a list of IDs of every checked row in the table
                let checkedIds = [];
                $(this).closest('table').find('input[type="checkbox"]:checked').each(function () {
                    let checkedText = $(this).closest('tr').find('.draggable').text().trim().toLowerCase();
                    if (checkedText === targetText || checkedText.replace(/\.[^/.]+$/, "") === targetText.replace(/\.[^/.]+$/, "") || checkedText === "◎" || targetText === "◎")
                        checkedIds.push($(this).data('id'));
                });
                let id = Number(e.dataTransfer.getData(e.dataTransfer.types[0]));
                // Add id to checkedIds only if it's not already in the list
                if (!checkedIds.includes(id)) {
                    checkedIds.push(id);
                }
                
                // Collect all AJAX calls
                let ajaxCalls = [];
                checkedIds.forEach(function (checkedId) {
                    if (checkedId !== toId) {
                        ajaxCalls.push(
                            $.ajax({
                                url: '/Api/LinkCallstack?id=' + checkedId + '&toId=' + toId,
                                processData: false,
                                contentType: false,
                                type: 'POST',
                            })
                        );
                    }
                });

                // Wait for all AJAX calls to complete before reloading
                $.when(...ajaxCalls).done(function () {
                    location.reload();
                });
            }


            return false;
        }
    })
})
function saveFixedVersion(id) {
    console.debug("Saving fixed version for " + id);
    let text = $('.modalTextInput').val();
    let buildType = $('#filterBuildTypes input:radio:checked').val();
    console.log(text + ' --> ' + buildType + ' --> ' + id);
    $.ajax({
        url: '/Api/SetFixedVersion?id=' + id + '&version=' + encodeURIComponent(text) +'&buildType=' + buildType,
        processData: false,
        contentType: false,
        type: 'POST',
        complete: function (data) {
            location.reload();
        },
    });
}
function saveApplicationVersion(id) {
    console.debug("Saving application version for " + id);
    let text = $('.modalAppVersionInput').val();
    let buildType = $('#filterAppBuildTypes input:radio:checked').val();
    console.log(text + ' --> ' + buildType + ' --> ' + id);
    $.ajax({
        url: '/Api/SetApplicationVersion?id=' + id + '&version=' + encodeURIComponent(text) +'&buildType=' + buildType,
        processData: false,
        contentType: false,
        type: 'POST',
        complete: function (data) {
            location.reload();
        },
    });
}
function saveTicket(id) {
    console.debug("Saving ticket for " + id);
    let text = $('.modalTicketInput').val();
    console.log(text + ' --> ' + id);
    $.ajax({
        url: '/Api/SetTicket?id=' + id + '&ticket=' + encodeURIComponent(text),
        processData: false,
        contentType: false,
        type: 'POST',
        complete: function (data) {
            location.reload();
        },
    });
}

function saveComment(id) {
    console.debug("Saving comment for " + id);
    let text = $('.modalCommentInput').val();
    console.log(text + ' --> ' + id);
    $.ajax({
        url: '/Api/SetComment?id=' + id + '&comment=' + encodeURIComponent(text),
        processData: false,
        contentType: false,
        type: 'POST',
        complete: function (data) {
            location.reload();
        },
    });
}

async function deleteOldEntries() {
    const versionInput = document.getElementById('deleteVersionInput');
    const version = versionInput.value.trim();
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
    
    if (!version) {
        alert('Please enter a version number');
        return;
    }

    if (!/^(\d+\.\d+\.\d+\.\d+)$/.test(version)) {
        alert('Please enter a valid version number in the format 1.2.3.4');
        return;
    }

    const confirmMessage = `Are you sure you want to delete all entries with version lower than ${version} that don't have tickets assigned?`;
    if (!confirm(confirmMessage)) {
        return;
    }

    inputSection.style.display = 'none';
    progressSection.style.display = 'block';
    resultMessage.style.display = 'none';
    startBtn.style.display = 'none';
    cancelBtn.disabled = true;
    closeBtn.disabled = true;

    progressText.textContent = 'Counting entries to delete...';
    progressCounter.textContent = '0 / ?';
    progressBar.style.width = '0%';
    progressBar.textContent = '0%';
    progressBar.setAttribute('aria-valuenow', 0);

    try {
        const countResponse = await $.ajax({
            url: '/Api/CountOldEntries?version=' + encodeURIComponent(version),
            type: 'GET'
        });

        const totalCount = countResponse.count;
        
        if (totalCount === 0) {
            progressSection.style.display = 'none';
            resultMessage.className = 'alert alert-info';
            resultMessage.textContent = 'No entries found to delete.';
            resultMessage.style.display = 'block';
            cancelBtn.disabled = false;
            closeBtn.disabled = false;
            return;
        }

        progressText.textContent = `Deleting ${totalCount} entries...`;
        progressCounter.textContent = `0 / ${totalCount}`;
        
        let deletedCount = 0;
        const batchSize = 10;
        const startTime = new Date();
        
        while (deletedCount < totalCount) {
            const batchResponse = await $.ajax({
                url: '/Api/DeleteOldEntries?version=' + encodeURIComponent(version) + '&batchSize=' + batchSize,
                type: 'POST'
            });

            deletedCount += batchResponse.deletedCount;
            
            const progress = Math.min(100, Math.round((deletedCount / totalCount) * 100));
            progressBar.style.width = progress + '%';
            progressBar.textContent = progress + '%';
            progressBar.setAttribute('aria-valuenow', progress);
            progressCounter.textContent = `${deletedCount} / ${totalCount}`;
            
            const elapsed = (new Date() - startTime) / 1000;
            const rate = deletedCount / elapsed;
            const remaining = totalCount - deletedCount;
            const eta = remaining > 0 ? Math.round(remaining / rate) : 0;
            
            progressDetails.textContent = `Deleted ${deletedCount} entries (${rate.toFixed(1)} per second). ETA: ${eta} seconds`;
            
            if (batchResponse.deletedCount === 0) {
                break;
            }
            
            await new Promise(resolve => setTimeout(resolve, 100));
        }

        progressBar.classList.remove('progress-bar-animated');
        progressSection.style.display = 'none';
        resultMessage.className = 'alert alert-success';
        resultMessage.textContent = `Successfully deleted ${deletedCount} entries.`;
        resultMessage.style.display = 'block';
        reloadBtn.style.display = 'inline-block';
        closeBtn.disabled = false;
        
    } catch (error) {
        progressSection.style.display = 'none';
        resultMessage.className = 'alert alert-danger';
        resultMessage.textContent = 'Error deleting entries: ' + (error.responseText || error.statusText || error);
        resultMessage.style.display = 'block';
        cancelBtn.disabled = false;
        closeBtn.disabled = false;
    }
}

$('#deleteOldEntriesModal').on('hidden.bs.modal', function () {
    document.getElementById('deleteInputSection').style.display = 'block';
    document.getElementById('deleteProgressSection').style.display = 'none';
    document.getElementById('deleteResultMessage').style.display = 'none';
    document.getElementById('deleteVersionInput').value = '';
    document.getElementById('deleteStartBtn').style.display = 'inline-block';
    document.getElementById('deleteReloadBtn').style.display = 'none';
    document.getElementById('deleteCancelBtn').disabled = false;
    document.getElementById('deleteModalCloseBtn').disabled = false;
});

