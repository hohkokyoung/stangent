from flask import Blueprint, jsonify, request

from shop.service import FastOrderService, OrderService
from .service import audit as audit_order

orders_bp = Blueprint("orders", __name__, url_prefix="/api/orders")


@orders_bp.route("/", methods=["GET", "POST"])
def list_orders():
    service = OrderService()
    if request.method == "POST":
        return jsonify(service.place(request.get_json()))
    return jsonify(service.all())


@orders_bp.route("/<order_id>/cancel", methods=["POST"])
def cancel(order_id, on_done=None):
    service = FastOrderService()
    service.place({"id": order_id})
    audit_order(order_id)
    on_done()
    handler = getattr(service, "place")
    handler({})
    return jsonify(cancelled=order_id)


def unknown_receiver(thing):
    thing.place({})  # project code has a `place`, so this may land there: ❓
    thing.frobnicate()  # nothing in the project is called that
