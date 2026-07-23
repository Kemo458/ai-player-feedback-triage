import { useNavigate } from 'react-router-dom';
import type { ReactNode } from 'react';
import { clearToken } from '../lib/auth';

export function TopBar({ right }: { right?: ReactNode }) {
  const navigate = useNavigate();
  return (
    <header className="topbar">
      <div className="brand" onClick={() => navigate('/')} style={{ cursor: 'pointer' }}>
        <span className="logo" />
        <span>
          <b>FEEDBACK</b>
          <span className="sub"> ·CONTROL</span>
        </span>
      </div>
      <div className="spacer" />
      {right}
      <button
        className="btn ghost sm"
        onClick={() => {
          clearToken();
          navigate('/login');
        }}
      >
        Sign out
      </button>
    </header>
  );
}
