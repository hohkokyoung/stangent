import { PriceTag } from './PriceTag';

function Drawer({ isOpen, children }: { isOpen: boolean; children?: unknown }) {
  if (!isOpen) {
    return null;
  }
  return <aside>{children}</aside>;
}

export function PriceModal({ open }: { open: boolean }) {
  return <Drawer isOpen={open}><PriceTag n={1} /></Drawer>;
}
