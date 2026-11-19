declare const connection: { on(name: string, f: (x: unknown) => void): void };

export function ShelfLive() {
  const listen = () => connection.on('ShelfChanged', () => {});
  return <button onClick={listen}>Live</button>;
}
