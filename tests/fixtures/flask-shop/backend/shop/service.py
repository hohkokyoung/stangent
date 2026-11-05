import logging

from .store import Store

log = logging.getLogger(__name__)


def audit(order_id):
    log.info("audit %s", order_id)


class OrderService:
    def __init__(self):
        self.store = Store()

    def all(self):
        return self.store.read_all()

    def place(self, order):
        self.check(order)
        return self.store.write(order)

    def check(self, order):
        audit(order.get("id"))

    @property
    def size(self):
        return len(self.all())


class FastOrderService(OrderService):
    def place(self, order):
        return super().place(order)


class Hidden(UnknownBase):  # noqa: F821
    def run(self):
        self.inherited()
