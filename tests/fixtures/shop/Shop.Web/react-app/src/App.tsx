import { Routes, Route } from 'react-router-dom';
import { StockPage } from './StockPage';
import { OrderPage } from './OrderPage';
import { PriceModal } from './PriceModal';

export function App() {
  return (
    <Routes>
      <Route path="/stock/:id" element={<StockPage id={1} />} />
      <Route path="/orders/new" element={<OrderPage />} />
      <Route path="/prices" element={<PriceModal open={false} />} />
    </Routes>
  );
}
