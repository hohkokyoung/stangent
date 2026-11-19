from flask import Flask, jsonify, render_template, request
from .admin import AccountView, admin_bp, reports_bp
from .orders import orders_bp
from .stock import stock_bp
from helpers import shout

open("ran.txt", "w").write("the extractor ran this file")  # it must never: the test looks for ran.txt

VERBS = ["GET", "PATCH"]

app = Flask(__name__)
app.register_blueprint(orders_bp)
app.register_blueprint(stock_bp, url_prefix="/api/stock")
admin_bp.register_blueprint(reports_bp, url_prefix="/reports")
app.register_blueprint(admin_bp, url_prefix="/admin")


def health():
    return jsonify(ok=True)


app.add_url_rule("/health", view_func=health)
app.add_url_rule("/accounts", view_func=AccountView.as_view("accounts"))


@app.route("/items/<int:item_id>", methods=["GET", "DELETE"])
def item(item_id):
    return jsonify(id=item_id, name=load_name(item_id))


def load_name(item_id):
    return shout(str(item_id))


@app.route("/files/<path:name>")
def files(name):
    return name


@app.route("/report", methods=VERBS)
def report():
    return render_template("report.html")


@app.get("/ping")
def ping():
    return request.args.get("x", "pong")


def make_url():
    return "/built-" + request.args["x"]


@app.route(make_url())
def built():
    return "built"
