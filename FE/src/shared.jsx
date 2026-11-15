import React, { useEffect, useRef, useState } from 'react';

export function Panel({ title, subtitle, children, ...props }) {
  return <section className="panel" {...props}><header className="panelhead"><h2>{title}</h2>{subtitle && <p>{subtitle}</p>}</header>{children}</section>;
}
export function Field({ label, children }) { return <label className="field"><span>{label}</span>{children}</label>; }
export function Tag({ children, ok }) { return <span className={`badge ${ok === true ? 'green' : ok === false ? 'amber' : ''}`}>{children}</span>; }
export function Empty({ children }) { return <div className="empty">{children}</div>; }
export function Notice({ error, message, busy }) {
  return <div aria-live="polite">{busy && <p className="loading" role="status">Đang xử lý…</p>}{error && <p className="alert error" role="alert">{error}</p>}{message && <p className="alert success" role="status">{message}</p>}</div>;
}
export function LoadState({ resource, children }) {
  if (resource.loading && resource.data === null) return <p className="loading" role="status">Đang tải dữ liệu…</p>;
  return <>{resource.loading && <p className="loading" role="status">Đang cập nhật…</p>}{resource.error && <div className="alert error" role="alert"><span>{resource.error}</span><button className="btn outline" type="button" onClick={resource.reload}>Thử lại</button></div>}{children}</>;
}

export function useLoad(loader, dependencies = []) {
  const [nonce, setNonce] = useState(0);
  const [state, setState] = useState({ data: null, loading: true, error: '' });
  useEffect(() => {
    const controller = new AbortController();
    setState(previous => ({ ...previous, loading: true, error: '' }));
    Promise.resolve().then(() => loader(controller.signal)).then(data => {
      if (!controller.signal.aborted) setState({ data, loading: false, error: '' });
    }).catch(error => {
      if (!controller.signal.aborted) setState(previous => ({ ...previous, loading: false, error: error.message || 'Không tải được dữ liệu.' }));
    });
    return () => controller.abort();
  }, [...dependencies, nonce]);
  return { ...state, reload: () => setNonce(value => value + 1) };
}

export function useAction() {
  const [state, setState] = useState({ busy: false, error: '', message: '' });
  const working = useRef(false), mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  async function run(action, success = '') {
    if (working.current) return { ok: false };
    working.current = true;
    setState({ busy: true, error: '', message: '' });
    try {
      const data = await action();
      if (mounted.current) setState({ busy: false, error: '', message: typeof success === 'function' ? success(data) : success });
      return { ok: true, data };
    } catch (error) {
      if (mounted.current) setState({ busy: false, error: error.message || 'Có lỗi xảy ra.', message: '' });
      return { ok: false };
    } finally { working.current = false; }
  }
  return { ...state, run };
}
