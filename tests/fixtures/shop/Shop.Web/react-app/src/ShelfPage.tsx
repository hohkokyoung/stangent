export function ShelfPage() {
  const save = () => fetch('/api/shelf', { method: 'PUT' });
  return <button onClick={save}>Save</button>;
}
