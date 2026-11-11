function priceLoader(currency: string) {
  return (n: number) => fetch(`/api/prices/${currency}/${n}`);
}

export function PriceTag({ n }: { n: number }) {
  const load = priceLoader('eur');
  const show = () => load(n);
  return <button onClick={show}>Price</button>;
}
