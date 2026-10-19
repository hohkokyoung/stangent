import { Routes } from '@angular/router';
import { OrderFormComponent } from './order-form.component';

export const routes: Routes = [
  {
    path: 'orders',
    component: OrderFormComponent,
    children: [{ path: ':id', loadComponent: () => import('./total.component').then(m => m.TotalComponent) }],
  },
];
