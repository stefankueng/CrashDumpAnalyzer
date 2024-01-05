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
        let btn = $(e.relatedTarget); // e.related here is the element that opened the modal, specifically the row button
        let id = btn.data('id'); // this is how you get the of any `data` attribute of an element
        $('.saveEdit').data('id', id); // then pass it to the button inside the modal
    })

    $('.saveEdit').on('click', function () {
        let id = $(this).data('id'); // the rest is just the same
        saveFixedVersion(id);
        $('#setFixedVersionModal').modal('toggle'); // this is to close the modal after clicking the modal button
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

