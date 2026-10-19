import { Component, Input } from '@angular/core';
import { Order } from './order';

@Component({ selector: 'app-total', template: '<span>{{ order?.total | money }}</span>' })
export class TotalComponent {
  @Input() order: Order | null = null;
}
