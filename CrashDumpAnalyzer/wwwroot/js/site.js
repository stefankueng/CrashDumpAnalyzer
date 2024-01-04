// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
$(document).on('click', '.collapsible', function () {
    $(this).toggleClass('collapsed');
    console.debug($(this).prev);
    $(this).prev().toggleClass('collapsed');
    $(this).next().toggleClass('collapsed');
});
