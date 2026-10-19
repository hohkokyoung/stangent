import { Component } from '@angular/core';
import { OrderService } from './order.service';
import { Order } from './order';

@Component({ selector: 'app-order-form', templateUrl: './order-form.component.html' })
export class OrderFormComponent {
  title = 'New order';
  order: Order | null = null;
  lastTotal = 0;

  constructor(private orders: OrderService) {}

  submit() {
    this.orders.place('web').subscribe(o => { this.order = o; this.lastTotal = o.total; });
  }
}
