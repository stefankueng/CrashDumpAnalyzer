// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
$(document).on('click', '.collapsible', function () {
    $(this).toggleClass('collapsed');
    console.debug($(this).prev);
    $(this).prev().toggleClass('collapsed');
    $(this).next().toggleClass('collapsed');
});

$(function () {
    $('#setFixedVersionModal').on('show.bs.modal', function (e) {
        $('.modalTextInput').val('');
        $('#fixedVersionError').html('');
        let btn = $(e.relatedTarget); // e.related here is the element that opened the modal (the button)
        let id = btn.data('id');
        $('.saveEdit').data('id', id); // then pass it to the button inside the modal
    })

    $('.saveEdit').on('click', function () {
        let id = $(this).data('id'); // the rest is just the same
        let text = $('.modalTextInput').val();
        if (/^(\d+\.\d+\.\d+\.\d+)$/.test(text))
            $('#fixedVersionError').html('');
        else
        {
            $('#fixedVersionError').html('Enter version number in the format 1.2.3.4');
            return;
        }
        saveFixedVersion(id);
        $('#setFixedVersionModal').modal('toggle'); // this is to close the modal after clicking the modal button
    })

    $('#setTicketModal').on('show.bs.modal', function (e) {
        $('.modalTicketInput').val('');
        let btn = $(e.relatedTarget); // e.related here is the element that opened the modal (the button)
        let id = btn.data('id');
        $('.saveTicket').data('id', id); // then pass it to the button inside the modal
    })

    $('.saveTicket').on('click', function () {
        let id = $(this).data('id'); // the rest is just the same
        let text = $('.modalTicketInput').val();
        saveTicket(id);
        $('#setTicketModal').modal('toggle'); // this is to close the modal after clicking the modal button
    })

    $('#setCommentModal').on('show.bs.modal', function (e) {
        $('.modalCommentInput').val('');
        let btn = $(e.relatedTarget); // e.related here is the element that opened the modal (the button)
        let id = btn.data('id');
        $('.saveComment').data('id', id); // then pass it to the button inside the modal
    })

    $('.saveComment').on('click', function () {
        console.debug("Saving comment");
        let id = $(this).data('id'); // the rest is just the same
        let text = $('.modalCommentInput').val();
        saveComment(id);
        $('#setCommentModal').modal('toggle'); // this is to close the modal after clicking the modal button
    })
})
function saveFixedVersion(id) {
    console.debug("Saving fixed version for " + id);
    let text = $('.modalTextInput').val();
    console.log(text + ' --> ' + id);
    $.ajax({
        url: 'Api/SetFixedVersion?id='+id+'&version='+text,
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
        url: 'Api/SetTicket?id='+id+'&ticket='+text,
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
        url: 'Api/SetComment?id='+id+'&comment='+text,
        processData: false,
        contentType: false,
        type: 'POST',
        complete: function (data) {
            location.reload();
        },
    });
}

