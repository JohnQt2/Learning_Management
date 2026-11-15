export function Loading({ text = 'Đang tải dữ liệu…' }: { text?: string }) {
  return <p className="state" role="status">{text}</p>;
}

export function ErrorBox({ message = 'Không tải được dữ liệu. Kiểm tra kết nối rồi thử lại.' }: { message?: string }) {
  return <p className="state state-error" role="alert">{message}</p>;
}

export function Empty({ text }: { text: string }) {
  return <p className="state">{text}</p>;
}
