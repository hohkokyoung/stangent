import { Routes, Route } from 'react-router-dom';
import { StockPage } from './StockPage';
import { OrderPage } from './OrderPage';
import { PriceModal } from './PriceModal';
import { ShelfEditor } from './ShelfEditor';

export function App() {
  return (
    <Routes>
      <Route path="/stock/:id" element={<StockPage id={1} />} />
      <Route path="/orders/new" element={<OrderPage />} />
      <Route path="/prices" element={<PriceModal open={false} />} />
      <Route path="/shelf/edit" element={<ShelfEditor />} />
    </Routes>
  );
}
