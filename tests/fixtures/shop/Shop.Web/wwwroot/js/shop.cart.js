// N5: a server-rendered app's own plain script: a jQuery call to an endpoint, bound to a view's button by its id.
function checkEmail() {
    $.ajax({ url: '/api/orders', type: 'POST' });
}

$(function () {
    $('#check-email').click(checkEmail);
});
