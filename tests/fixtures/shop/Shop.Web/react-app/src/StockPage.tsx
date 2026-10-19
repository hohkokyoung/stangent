export function StockPage({ id }: { id: number }) {
  const load = () => fetch(`/api/stock/${id}`);
  return <button onClick={load}>Stock</button>;
}
