// D221: a menu written as data, the way Sonarr's sidebar is: headings, items, children.
const GROUPS = [
  { label: 'Stock', items: [
    { title: 'Shelves', to: '/stock/1', children: [{ title: 'Prices', to: '/prices' }] },
    { title: 'New order', to: '/orders/new' },
  ] },
  { title: 'Orders', to: '/orders/new' },
  { title: 'Help', to: 'https://example.com/help' },
];

export function Sidebar() {
  return <ul>{GROUPS.map((g) => <li key={String(g.label ?? g.title)}>{String(g.label ?? g.title)}</li>)}</ul>;
}
