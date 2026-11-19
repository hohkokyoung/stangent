from flask import Blueprint, jsonify
from flask.views import MethodView

admin_bp = Blueprint("admin", __name__)
reports_bp = Blueprint("reports", __name__)
forgotten_bp = Blueprint("forgotten", __name__, url_prefix="/forgotten")


@reports_bp.route("/daily")
def daily():
    return jsonify([])


@forgotten_bp.route("/page")
def forgotten_page():
    return jsonify([])


class AccountView(MethodView):
    def get(self):
        return jsonify([])

    def post(self):
        return jsonify({})


def register(thing):
    @thing.route("/on-a-parameter")
    def on_a_parameter():
        return ""
