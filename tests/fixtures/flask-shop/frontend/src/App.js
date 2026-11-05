import React from "react";
import { BrowserRouter, Route, Switch } from "react-router-dom";
import config from "./config";

function loadOrders() {
  const url = `${config.api}/orders`;
  return fetch(url, { method: "POST", body: "{}" });
}

function cancelOrder(id) {
  return fetch(`${config.api}/orders/${id}/cancel`, { method: "POST" });
}

function loadItem(id) {
  return fetch(`${config.host}/items/${id}`);
}

function loadLevels() {
  return fetch("http://127.0.0.1:5000/api/stock/levels");
}

function loadNothing() {
  return fetch(`${config.api}/no-such-route`);
}

function loadBuilt(where) {
  return fetch(where);
}

function Orders() {
  loadOrders();
  cancelOrder(1);
  loadItem(1);
  loadLevels();
  loadNothing();
  loadBuilt("/x");
  return <div>Orders</div>;
}

export default function App() {
  return (
    <BrowserRouter>
      <Switch>
        <Route exact path="/orders" component={Orders} />
      </Switch>
    </BrowserRouter>
  );
}
