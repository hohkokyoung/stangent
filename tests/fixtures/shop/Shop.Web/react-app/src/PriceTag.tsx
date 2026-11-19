function priceLoader(currency: string) {
  return (n: number) => fetch(`/api/prices/${currency}/${n}`);
}

function refreshPrices() {
  return fetch('/api/prices');
}

export function PriceTag({ n }: { n: number }) {
  const load = priceLoader('eur');
  const show = () => load(n);
  const poll = () => setInterval(() => refreshPrices(), 60000);
  return <div><button onClick={show}>Price</button><button onClick={poll}>Poll</button></div>;
}
