// propresenter-utilities v1: switches several outputs from one request, so they change together.
// GET /script/<id>/set?ids=0,1,2,3&on=true[&toggle_after=30] -> {"results":[null|"error",...]}
function query(q) {
  let r = {};
  if (!q) return r;
  let parts = q.split("&");
  for (let i = 0; i < parts.length; i++) {
    let kv = parts[i].split("=");
    r[kv[0]] = kv.length > 1 ? kv[1] : "";
  }
  return r;
}
function done(result, code, message, ud) {
  ud.s.results[ud.i] = code === 0 ? null : message;
  ud.s.left--;
  if (ud.s.left === 0) {
    ud.s.response.headers = [["Content-Type", "application/json"]];
    ud.s.response.body = JSON.stringify({ results: ud.s.results });
    ud.s.response.send();
  }
}
function set(request, response) {
  let q = query(request.query);
  let ids = (q.ids || "").split(",");
  let s = { response: response, results: [], left: ids.length };
  if (!q.ids) { response.code = 400; response.body = "ids required"; response.send(); return; }
  for (let i = 0; i < ids.length; i++) {
    let p = { id: JSON.parse(ids[i]), on: q.on === "true" };
    if (q.toggle_after) p.toggle_after = JSON.parse(q.toggle_after);
    Shelly.call("Switch.Set", p, done, { s: s, i: i });
  }
}
HTTPServer.registerEndpoint("set", set);
