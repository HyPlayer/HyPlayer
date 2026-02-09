import os
import chardet

def convert_to_utf8(folder_path, extensions=('.csproj', '.cs', '.xaml')):
    for root, dirs, files in os.walk(folder_path):
        for file in files:
            if file.endswith(extensions):
                file_path = os.path.join(root, file)
                try:
                    # 1. 检测文件编码
                    with open(file_path, 'rb') as f:
                        raw_data = f.read()
                        result = chardet.detect(raw_data)
                        encoding = result['encoding']

                    # 2. 如果不是UTF-8，则进行转换
                    if encoding and encoding.lower() != 'utf-8-sig':
                        print(f"Converting: {file_path} (From {encoding} to UTF-8-SIG)")
                        
                        # 3. 读取并转换
                        content = raw_data.decode(encoding, errors='ignore')
                        
                        # 4. 覆盖写入UTF-8
                        with open(file_path, 'w', encoding='utf-8-sig', newline='\n') as f:
                            f.write(content)
                    else:
                        print(f"Skipping: {file_path} (Already UTF-8-SIG or unknown)")

                except Exception as e:
                    print(f"Error processing {file_path}: {e}")
    
current_path = os.path.dirname(os.path.abspath(__file__))
convert_to_utf8(current_path)
