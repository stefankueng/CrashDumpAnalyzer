function postCallstackAction(action, parameters) {
    return $.ajax({
        url: '/Api/' + action + '?' + $.param(parameters),
        processData: false,
        contentType: false,
        type: 'POST'
    });
}

function reloadAfterActions(...requests) {
    $.when(...requests).done(() => location.reload()).fail(error => {
        alert('The action failed: ' + (error.responseText || error.statusText || 'Unknown error'));
    });
}

function deleteCallstack(id, reallyDelete) {
    reloadAfterActions(postCallstackAction('DeleteDumpCallstack', { id, reallyDelete: Boolean(reallyDelete) }));
}

function deleteDump(callstackId, dumpId) {
    reloadAfterActions(postCallstackAction('DeleteDumpFile', { dumpId }));
}

function unlinkCallstack(id) {
    reloadAfterActions(postCallstackAction('UnlinkDumpCallstack', { id }));
}

function reAnalyzeDump(callstackId) {
    reloadAfterActions(postCallstackAction('ReAnalyzeDumpFile', { callstackId }));
}

$(document).ready(function () {
    $('.btn').each(function () {
        if (Array.from($(this).text().trim()).length === 1) {
            $(this).addClass('single-char');
        }
    });
    const checkbox = document.getElementById('flexSwitchCheckDefault');
    checkbox.checked = document.documentElement.getAttribute('data-bs-theme') === 'dark';
    checkbox.addEventListener('change', () => {
        const theme = checkbox.checked ? 'dark' : 'light';
        document.documentElement.setAttribute('data-bs-theme', theme);
        localStorage.setItem('theme', theme);
        initTooltips();
    });
});

const popoverTriggerList = document.querySelectorAll('[data-bs-toggle="popover"]');
const popoverList = [...popoverTriggerList].map(popoverTriggerEl => {
    const popover = new bootstrap.Popover(popoverTriggerEl, { html: true, sanitize: false, container: 'body', trigger: 'manual' });
    popoverTriggerEl.setAttribute('aria-expanded', 'false');
    popoverTriggerEl.addEventListener('shown.bs.popover', () => popoverTriggerEl.setAttribute('aria-expanded', 'true'));
    popoverTriggerEl.addEventListener('hidden.bs.popover', () => popoverTriggerEl.setAttribute('aria-expanded', 'false'));
    popoverTriggerEl.addEventListener('click', event => {
        event.preventDefault();
        popoverList.forEach(other => {
            if (other !== popover) other.hide();
        });
        popover.toggle();
    });
    popoverTriggerEl.addEventListener('keydown', event => {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            popoverTriggerEl.click();
        } else if (event.key === 'Tab' && !event.shiftKey && popoverTriggerEl.getAttribute('aria-expanded') === 'true') {
            const panel = document.getElementById(popoverTriggerEl.getAttribute('aria-describedby'));
            const firstControl = panel?.querySelector('a[href], button');
            if (firstControl) {
                event.preventDefault();
                firstControl.focus();
            }
        }
    });
    return popover;
});

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

document.addEventListener('click', event => {
    if (!event.target.closest('[data-bs-toggle="popover"], .popover')) {
        popoverList.forEach(popover => popover.hide());
    }
});
document.addEventListener('keydown', event => {
    if (event.key === 'Escape') {
        popoverTriggerList.forEach((trigger, index) => {
            if (trigger.getAttribute('aria-expanded') === 'true') {
                const panel = document.getElementById(trigger.getAttribute('aria-describedby'));
                if (trigger === document.activeElement || panel?.contains(document.activeElement)) {
                    trigger.focus();
                    event.preventDefault();
                }
                popoverList[index].hide();
            }
        });
    }
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

    reloadAfterActions(...checkedIds.map(id =>
        postCallstackAction('DeleteDumpCallstack', { id, reallyDelete: Boolean(reallyDelete) })));
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

    const [toId, ...sourceIds] = checkedIds;
    reloadAfterActions(...sourceIds.map(id => postCallstackAction('LinkCallstack', { id, toId })));
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
        let ticket = btn.closest('td').find('.ticket').map(function() { return $(this).text().trim(); }).get().join(' ');
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
                
                reloadAfterActions(...checkedIds.filter(checkedId => checkedId !== toId)
                    .map(id => postCallstackAction('LinkCallstack', { id, toId })));

            }


            return false;
        }
    })
})
function saveFixedVersion(id) {
    reloadAfterActions(postCallstackAction('SetFixedVersion', {
        id, version: $('.modalTextInput').val(), buildType: $('#filterBuildTypes input:radio:checked').val()
    }));
}

function saveApplicationVersion(id) {
    reloadAfterActions(postCallstackAction('SetApplicationVersion', {
        id, version: $('.modalAppVersionInput').val(), buildType: $('#filterAppBuildTypes input:radio:checked').val()
    }));
}

function saveTicket(id) {
    reloadAfterActions(postCallstackAction('SetTicket', { id, ticket: $('.modalTicketInput').val() }));
}

function saveComment(id) {
    reloadAfterActions(postCallstackAction('SetComment', { id, comment: $('.modalCommentInput').val() }));
}
