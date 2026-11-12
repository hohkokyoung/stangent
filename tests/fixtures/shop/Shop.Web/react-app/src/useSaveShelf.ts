// Step 6 (fresh check S4, S7): `mutate` taken from the app's own mutation hook, called inside the same hook.
import { useMutation } from '@tanstack/react-query';

const send = (url: string) => fetch(url, { method: 'PUT' });

export function useApiMutation(path: string) {
  return useMutation({ mutationFn: (id: number) => send(`${path}/${id}`) });
}

export function useSaveShelf() {
  const { mutate } = useApiMutation('/api/shelf');
  const saveShelf = (id: number) => {
    mutate(id);
  };
  return { saveShelf };
}
