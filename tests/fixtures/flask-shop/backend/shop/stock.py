from flask import Blueprint, jsonify

stock_bp = Blueprint("stock", __name__, url_prefix="/ignored-when-registered-with-its-own")


@stock_bp.route("/levels")
def levels():
    return jsonify([])
