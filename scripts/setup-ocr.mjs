import {mkdir,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const directory='runtime/win-x64-0.3.0-final/tessdata';await mkdir(directory,{recursive:true});
const sha='87416418657359cb625c412a48b6e1d6d41c29bd';
const manifest={repository:'https://github.com/tesseract-ocr/tessdata_fast',commit:sha,files:[]};
for(const name of ['eng.traineddata','chi_sim.traineddata','LICENSE']) {
  const url=`https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/${sha}/${name}`;
  const r=await fetch(url);if(!r.ok)throw Error(`模型下载失败 ${r.status}`);
  const bytes=Buffer.from(await r.arrayBuffer());await writeFile(`${directory}/${name}`,bytes);
  manifest.files.push({name,url,sha256:createHash('sha256').update(bytes).digest('hex')});
}
await writeFile(`${directory}/manifest.json`,JSON.stringify(manifest,null,2));
console.log('中英文 OCR 模型及许可证已下载并记录版本和 SHA-256');
