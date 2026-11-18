import {mkdir,readFile,writeFile,cp} from 'node:fs/promises';
import {createHash} from 'node:crypto';
await mkdir('licenses',{recursive:true});
const sources={
  'tesseract-wrapper.txt':'https://raw.githubusercontent.com/charlesw/tesseract/master/LICENSE.txt',
  'tesseract-engine.txt':'https://raw.githubusercontent.com/tesseract-ocr/tesseract/5.2.0/LICENSE',
  'leptonica.txt':'https://raw.githubusercontent.com/DanBloomberg/leptonica/1.82.0/leptonica-license.txt',
};
const manifest=[];
for(const [file,url] of Object.entries(sources)){
  const r=await fetch(url);if(!r.ok)throw Error(`许可证下载失败 ${url}: ${r.status}`);
  const data=Buffer.from(await r.arrayBuffer());await writeFile(`licenses/${file}`,data);manifest.push({file,url,sha256:createHash('sha256').update(data).digest('hex')});
}
for(const [pkg,version] of [['tesseract','5.2.0'],['microsoft.windows.sdk.net.ref','10.0.19041.56']])
  await cp(`.build/nuget/${pkg}/${version}/${pkg}.nuspec`,`licenses/${pkg}.nuspec`);
await writeFile('licenses/manifest.json',JSON.stringify(manifest,null,2));
