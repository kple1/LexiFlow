'use strict';
(() => {
  const byId = id => document.getElementById(id);
  let token = '';
  let mode = 'forgot';
  let busy = false;
  let enabled = false;
  const message = (text, state = '') => {
    byId('message').textContent = text;
    byId('message').className = state;
  };
  const configure = () => {
    const parts = location.hash.slice(1).split('/');
    mode = ['signup', 'forgot', 'verify-email', 'reset-password'].includes(parts[0]) ? parts[0] : 'forgot';
    token = parts.length === 2 && /^[A-F0-9]{64}$/.test(parts[1]) ? parts[1] : '';
    // Remove bearer immediately; do not store it in local/session storage or logs.
    if (parts.length > 1) history.replaceState(null, '', location.pathname);
    byId('password').value = byId('confirm').value = '';
    const isRequest = mode === 'forgot' || (mode === 'signup' && !token);
    byId('email-field').hidden = !isRequest;
    byId('email').required = isRequest;
    byId('username-field').hidden = !(mode === 'signup' && token);
    byId('username').required = mode === 'signup' && Boolean(token);
    byId('password-fields').hidden = !(token && (mode === 'signup' || mode === 'reset-password'));
    byId('password').required = byId('confirm').required = !byId('password-fields').hidden;
    const content = {
      signup: token ? ['가입 완료하기', '사용할 아이디와 비밀번호를 정해 주세요. 완료 버튼을 누르면 이메일 링크를 검증하고 계정을 만듭니다.', '인증하고 가입 완료']
        : ['새 계정 만들기', '본인 이메일로 인증 링크를 받은 뒤 아이디와 비밀번호를 설정합니다.', '가입 인증메일 받기'],
      forgot: ['비밀번호 찾기', '계정에 미리 인증한 복구 이메일을 입력하세요. 이메일이 없는 기존 계정은 로그인 후 먼저 등록해야 합니다.', '재설정 메일 받기'],
      'verify-email': ['복구 이메일 확인', '본인이 요청한 이메일 연결인지 확인하세요. 완료하면 모든 기기에서 다시 로그인해야 합니다.', '이 이메일 연결 확인'],
      'reset-password': ['새 비밀번호 설정', '이 링크는 한 번만 사용할 수 있습니다. 변경하면 모든 기기에서 로그아웃됩니다.', '비밀번호 재설정']
    }[mode];
    byId('heading').textContent = content[0];
    byId('description').textContent = content[1];
    byId('submit').textContent = content[2];
    byId('submit').disabled = !enabled || busy || (['verify-email', 'reset-password'].includes(mode) && !token);
    if (['verify-email', 'reset-password'].includes(mode) && !token)
      message('링크가 없거나 올바르지 않습니다. 메일의 원래 링크를 다시 열거나 새 메일을 요청하세요.', 'error');
    else message(enabled ? '본인이 요청한 경우에만 계속 진행해 주세요.' : '서버 상태를 확인하고 있습니다…');
  };
  byId('account-form').addEventListener('submit', async event => {
    event.preventDefault();
    if (busy || !enabled) return;
    const password = byId('password').value;
    if (!byId('password-fields').hidden && (password !== byId('confirm').value || password.length < 12 || new TextEncoder().encode(password).length > 72)) {
      message('비밀번호 두 개가 일치해야 하며 12자 이상·UTF-8 72바이트 이내여야 합니다.', 'error');
      return;
    }
    let path, body;
    if (mode === 'signup' && token) { path = 'signup/complete'; body = { token, userId: byId('username').value, pw: password }; }
    else if (mode === 'signup') { path = 'signup/request'; body = { email: byId('email').value }; }
    else if (mode === 'forgot') { path = 'password/request'; body = { email: byId('email').value }; }
    else if (mode === 'verify-email' && token) { path = 'email/confirm'; body = { token }; }
    else if (mode === 'reset-password' && token) { path = 'password/reset'; body = { token, pw: password }; }
    else { message('원래 메일 링크를 다시 열어 주세요.', 'error'); return; }
    busy = true;
    byId('submit').disabled = true;
    let finished = false;
    try {
      const response = await fetch('/account/' + path, { method: 'POST', credentials: 'omit', cache: 'no-store', redirect: 'error',
        headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal: AbortSignal.timeout(20000) });
      let result = {};
      try { result = await response.json(); } catch { }
      message(response.status === 429 ? '요청이 많습니다. 잠시 후 다시 시도해 주세요.'
        : result.message || (response.ok ? '요청이 완료되었습니다.' : '처리하지 못했습니다. 잠시 후 다시 시도해 주세요.'), response.ok ? 'success' : 'error');
      if (response.ok && token) { token = ''; finished = true; byId('account-form').hidden = true; }
    } catch {
      message('서버 연결이 끊겼습니다. 완료 여부를 확인한 후 다시 시도하세요. 링크가 이미 처리됐을 수 있습니다.', 'error');
    } finally {
      byId('password').value = byId('confirm').value = '';
      busy = false;
      byId('submit').disabled = finished || !enabled;
    }
  });
  // A new mail link/navigation must not inherit an in-flight request's mode or
  // token. Reload the same-origin page; fragments never reach the server.
  window.addEventListener('hashchange', () => location.reload());
  configure();
  fetch('/account/availability', { credentials: 'omit', cache: 'no-store', redirect: 'error', signal: AbortSignal.timeout(10000) })
    .then(response => { if (!response.ok) throw new Error(); return response.json(); })
    .then(result => {
      enabled = result.enabled === true;
      // Do not re-read the fragment: its token was intentionally removed above.
      const missingToken = ['verify-email', 'reset-password'].includes(mode) && !token;
      byId('submit').disabled = !enabled || missingToken;
      if (missingToken) message('링크가 없거나 올바르지 않습니다. 메일의 원래 링크를 다시 열거나 새 메일을 요청하세요.', 'error');
      else message(enabled ? '본인이 요청한 경우에만 계속 진행해 주세요.' : '이메일 기능을 준비 중입니다. 기존 계정은 앱에서 계속 로그인할 수 있습니다.', enabled ? '' : 'error');
    })
    .catch(() => message('서버 상태를 확인하지 못했습니다. 잠시 후 다시 열어 주세요.', 'error'));
})();
