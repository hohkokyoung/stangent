import { Component } from '@angular/core';

// D221: a menu in markup, the way Kavita's side nav is: link= and routerLink, through @if.
@Component({
  selector: 'app-nav',
  template: `<div class="nav">
    <app-nav-item title="Orders" link="/orders"></app-nav-item>
    @if (admin) { <a routerLink="/orders/new">New order</a> }
    <a [routerLink]="['/', 'stock']">Stock</a>
  </div>`,
})
export class NavComponent { admin = true; }
