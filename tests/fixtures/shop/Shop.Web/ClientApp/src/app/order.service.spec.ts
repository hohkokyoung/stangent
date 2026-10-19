import { OrderService } from './order.service';

declare function describe(name: string, body: () => void): void;
declare function it(name: string, body: () => void): void;

describe('OrderService', () => {
  it('places an order', () => {
    new OrderService(null as any).place('ann');
  });
});
