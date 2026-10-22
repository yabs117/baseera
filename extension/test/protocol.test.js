import test from "node:test";
import assert from "node:assert/strict";
import {validateTranslations} from "../protocol.mjs";
test("reorders responses by exact id",()=>assert.deepEqual(validateTranslations([{id:1,text:"ب"},{id:0,text:"أ"}],["a","b"]),["أ","ب"]));
test("rejects missing segments",()=>assert.throws(()=>validateTranslations([], ["a"])));
test("rejects duplicate identifiers",()=>assert.throws(()=>validateTranslations([{id:0,text:"a"},{id:0,text:"b"}],["a","b"])));
test("rejects out of range ids",()=>assert.throws(()=>validateTranslations([{id:5,text:"a"}],["a"])));
test("treats markup as literal text",()=>assert.deepEqual(validateTranslations([{id:0,text:"<script>not executable</script>"}],["a"]),["<script>not executable</script>"]));
