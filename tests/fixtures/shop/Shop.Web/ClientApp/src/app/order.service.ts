import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../environments/environment';
import { Order } from './order';

@Injectable({ providedIn: 'root' })
export class OrderService {
  baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  place(customer: string) {
    return this.http.post<Order>(this.baseUrl + 'orders', { customer });
  }
}
