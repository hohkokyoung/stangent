// D316: opening the editor runs useSaveShelf, which only sets the shelf's save up (a mutation); the Save click sends it.
import { useSaveShelf } from './useSaveShelf';

export function ShelfEditor() {
  const { saveShelf } = useSaveShelf();
  const save = () => saveShelf(1);
  return <button onClick={save}>Save shelf</button>;
}
