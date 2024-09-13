// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
$(document).on('click', '.collapsible', function () {
    $(this).toggleClass('collapsed');
    console.debug($(this).prev);
    $(this).prev().toggleClass('collapsed');
    $(this).next().toggleClass('collapsed');
});
$(document).ready(function () {
    $('.btn').each(function () {
        if (Array.from($(this).text().trim()).length === 1) {
            $(this).addClass('single-char');
        }
    });
});

$(function () {
    $('#setFixedVersionModal').on('show.bs.modal', function (e) {
        $('.modalTextInput').val('');
        $('#fixedVersionError').html('');
        let btn = $(e.relatedTarget); // e.related here is the element that opened the modal (the button)
        let id = btn.data('id');
        $('.saveEdit').data('id', id); // then pass it to the button inside the modal
        let version = btn.closest('td').find('.version').text().trim();
        $('.modalTextInput').val(version);
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
            if (e.dataTransfer.types[0] === this.textContent.toLowerCase()) {
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
            if (e.dataTransfer.types[0] === this.textContent.toLowerCase()) {
                let toId = $(this).data('id'); // the rest is just the same
                let id = Number(e.dataTransfer.getData(e.dataTransfer.types[0]));
                if (id !== toId) {
                    $.ajax({
                        url: '/Api/LinkCallstack?id=' + id + '&toId=' + toId,
                        processData: false,
                        contentType: false,
                        type: 'POST',
                        complete: function (data) {
                            location.reload();
                        },
                    });
                }
            }


            return false;
        }
    })
})
function saveFixedVersion(id) {
    console.debug("Saving fixed version for " + id);
    let text = $('.modalTextInput').val();
    console.log(text + ' --> ' + id);
    $.ajax({
        url: '/Api/SetFixedVersion?id=' + id + '&version=' + encodeURIComponent(text),
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